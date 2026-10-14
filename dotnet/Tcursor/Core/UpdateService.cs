using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tcursor.Core;

/// <summary>一次更新检查的结果（来自 Release 元数据）</summary>
internal sealed class UpdateInfo
{
    public string Tag { get; init; } = "";
    public string Version { get; init; } = "";
    public string AssetName { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
    public bool Newer { get; init; }
}

/// <summary>安装「已就绪」更新包的结果（调用方按结果分支，勿用错误文案判断语义）</summary>
internal enum PreparedInstallResult
{
    /// <summary>已交给更新器（本进程退出即可）</summary>
    Started,
    /// <summary>本机没有已下载就绪的更新包</summary>
    NothingPrepared,
    /// <summary>失败（包不完整 / 更新器启动失败），原因见 error</summary>
    Failed,
}

/// <summary>
/// 自动更新（检查 → 下载校验 → 应用重启）。便携单文件软件的完整更新链：
///
/// 1) 检查：GitHub Releases 的 latest 元数据（可用环境变量 <c>TCURSOR_UPDATE_FEED</c> 覆盖，便于测试/镜像）；
/// 2) 下载：包名优先取标准软件名 <c>Tcursor.zip</c>（否则取第一个 .zip），并用发布描述里的
///    “zip sha256: …” 做完整性校验——HTTPS 之外的第二道防线（防下载损坏 / 代理干扰）；
/// 3) 应用：把**自身**复制成临时目录里的更新器进程（`--apply-update`）→ 主进程退出 → 更新器等旧进程
///    完全退出后替换 exe（运行中的 exe 不能被自己覆盖，但可以被另一个没有占用它的进程覆盖）→ 重启新版本。
///
/// 用户数据（config.json / portable.txt / 规则等）从不进入替换范围；替换前先探测目标目录可写，
/// 不可写（只读介质 / 受保护目录）时给出可操作的中文提示而不是失败在最后一步。
/// </summary>
internal static class UpdateService
{
    /// <summary>默认更新源（GitHub Releases 最新版）</summary>
    public const string DefaultFeed = "https://api.github.com/repos/Crucialmer/Tcursor/releases/latest";

    /// <summary>发布页（手动下载入口；自动更新不可用时提示用）</summary>
    public const string ReleasesPage = "https://github.com/Crucialmer/Tcursor/releases/latest";

    /// <summary>标准包名（发布约定：包名 = 标准软件名）</summary>
    private const string PackageName = "Tcursor.zip";

    /// <summary>更新后不自动重启（测试/自动化用）</summary>
    private const string NoRestartEnv = "TCURSOR_UPDATE_NO_RESTART";

    /// <summary>替换前等待旧进程退出的上限</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(60);

    private static readonly Regex ZipHashRegex =
        new(@"zip\s*sha256\s*[:：]\s*`?([0-9a-fA-F]{64})`?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HttpClient Http = CreateClient(TimeSpan.FromSeconds(20));
    private static readonly HttpClient HttpFile = CreateClient(TimeSpan.FromMinutes(10));   // 下载：慢线路也不该被总超时掐断

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        // ConnectTimeout：连接建立阶段的硬超时（DNS 卡顿/链路黑洞在 TUN/代理环境下可能不受 HttpClient.Timeout 约束）
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Tcursor/" + AppInfo.Version);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>当前更新源</summary>
    public static string Feed
        => Environment.GetEnvironmentVariable("TCURSOR_UPDATE_FEED") is { Length: > 0 } custom ? custom : DefaultFeed;

    /// <summary>更新工作目录（下载与解压的临时位置）</summary>
    public static string WorkDir => Path.Combine(Path.GetTempPath(), "Tcursor.Update");

    /// <summary>是否有可自动更新的目标（便携模式 = 程序目录可写；只读位置返回 false 与原因）</summary>
    public static bool CanSelfUpdate(out string error)
    {
        error = "";
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe is null)
            {
                error = "无法定位程序文件";
                return false;
            }
            string dir = Path.GetDirectoryName(exe)!;
            string probe = Path.Combine(dir, ".tcursor-write-test");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            error = "程序目录不可写（可能在只读介质或受保护目录中）：" + ex.Message;
            return false;
        }
    }

    // ---------------- 检查 ----------------

    /// <summary>检查更新（网络调用，必须在后台线程执行）。返回 null 时 <paramref name="error"/> 给出原因。
    /// 超时采用「取消式」：到点先取消在途请求再收割任务，避免把仍在 I/O 的任务丢在关停路径上。</summary>
    public static UpdateInfo? Check(out string error, string? feed = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        Task<(UpdateInfo? Info, string Error)> task = Task.Run(() => CheckCore(feed ?? Feed, timeout.Token));
        if (!task.Wait(TimeSpan.FromSeconds(30)))
        {
            timeout.Cancel();   // 让在途请求真正结束（HttpClient 内部超时之外的第二道闸门）
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);   // 观察异常，避免未观测异常
            error = "更新服务响应超时（30 秒）";
            return null;
        }
        (UpdateInfo? info, string err) = task.Result;
        error = err;
        return info;
    }

    private static (UpdateInfo? Info, string Error) CheckCore(string feed, CancellationToken token)
    {
        try
        {
            string json = Http.GetStringAsync(feed, token).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string tag = GetString(root, "tag_name");
            string body = GetString(root, "body");
            string version = NormalizeVersion(tag);

            // 资产：优先标准包名，其次第一个 .zip
            string assetName = "", url = "";
            long size = 0;
            if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                JsonElement? chosen = null;
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = GetString(asset, "name");
                    if (name.Equals(PackageName, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = asset;
                        break;
                    }
                    if (chosen is null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        chosen = asset;
                }
                if (chosen is { } pick)
                {
                    assetName = GetString(pick, "name");
                    url = GetString(pick, "browser_download_url");
                    if (pick.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long sizeValue))
                        size = sizeValue;
                }
            }

            Match m = ZipHashRegex.Match(body);
            string sha = m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";

            if (version.Length == 0 || url.Length == 0)
                return (null, "更新源返回的版本或下载地址不完整");

            return (new UpdateInfo
            {
                Tag = tag,
                Version = version,
                AssetName = assetName,
                DownloadUrl = url,
                Sha256 = sha,
                Size = size,
                Newer = CompareVersions(version, AppInfo.Version) > 0,
            }, "");
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return (null, Friendly(ex));
        }
    }

    private static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException => "无法连接更新服务（网络不可用或服务不可达）",
        TaskCanceledException => "更新服务响应超时",
        OperationCanceledException => "更新服务响应超时",
        JsonException => "更新服务返回了无法解析的数据",
        _ => ex.Message,
    };

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>版本号规范化：去掉前缀 v、只保留数字与点（"v1.1" → "1.1"）</summary>
    public static string NormalizeVersion(string tag)
    {
        string s = (tag ?? "").Trim();
        int i = 0;
        while (i < s.Length && !char.IsDigit(s[i]))
            i++;
        int j = i;
        while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.'))
            j++;
        return s[i..j].TrimEnd('.');
    }

    /// <summary>版本比较：a &gt; b 返回正数，相等 0，小于负数（缺位按 0；"1.10" &gt; "1.9"）</summary>
    public static int CompareVersions(string a, string b)
    {
        int[] pa = ParseParts(a), pb = ParseParts(b);
        for (int i = 0; i < 3; i++)
        {
            if (pa[i] != pb[i])
                return pa[i].CompareTo(pb[i]);
        }
        return 0;

        static int[] ParseParts(string v)
        {
            var parts = new int[3];
            string[] tokens = (v ?? "").Split('.');
            for (int i = 0; i < 3 && i < tokens.Length; i++)
                _ = int.TryParse(tokens[i], out parts[i]);
            return parts;
        }
    }

    // ---------------- 下载 ----------------

    /// <summary>下载更新包（已存在且哈希相符时直接复用）。返回包路径；失败返回 null 并给出原因。</summary>
    public static string? Download(UpdateInfo info, IProgress<(long Done, long Total)>? progress, CancellationToken token, out string error)
    {
        error = "";
        try
        {
            // 无校验值一律不下载（旧行为：无哈希也下载并报「已校验通过」，但退出/启动时安装又要求 64 位哈希
            // → 包永远装不上、每次检查都重下 46MB）。官方发布脚本会自动在描述里写 zip sha256，正常源不会命中这里。
            if (info.Sha256.Length != 64)
            {
                error = "更新源未提供 sha256 校验值：出于安全考虑不自动下载，请从官方发布页手动更新";
                Log.Info("下载跳过：" + error);
                return null;
            }
            Directory.CreateDirectory(WorkDir);
            string package = Path.Combine(WorkDir, PackageName);

            if (File.Exists(package) && Sha256Of(package).Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                return package;   // 上次已下好：直接复用

            using var response = HttpFile.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token)
                .GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? info.Size;

            // 临时名按下载实例唯一：避免「后台静默下载」与「手动下载」并发时在 File.Create 上互相撞锁
            string tmp = package + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
            using (Stream source = response.Content.ReadAsStream(token))
            using (var sink = File.Create(tmp))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[64 * 1024];
                long done = 0;
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    sink.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    progress?.Report((done, total));
                }
                string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!actual.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"下载校验失败：sha256 不符（期望 {info.Sha256[..12]}…，实际 {actual[..12]}…）";
                    TryDelete(tmp);
                    return null;
                }
            }
            File.Move(tmp, package, overwrite: true);
            return package;
        }
        catch (OperationCanceledException)
        {
            error = "已取消";
            return null;
        }
        catch (Exception ex)
        {
            error = Friendly(ex);
            Log.Error(ex);
            return null;
        }
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    // ---------------- 应用 ----------------

    /// <summary>启动更新器（自身副本）完成替换（可选重启）；主进程随后应立即退出。</summary>
    public static bool SpawnUpdater(string packagePath, out string error, bool restart = true)
    {
        error = "";
        try
        {
            string? self = Environment.ProcessPath;
            if (self is null)
            {
                error = "无法定位程序文件";
                return false;
            }
            if (!CanSelfUpdate(out error))
                return false;

            Directory.CreateDirectory(WorkDir);
            string updater = Path.Combine(WorkDir, "Tcursor.updater.exe");
            File.Copy(self, updater, overwrite: true);
            var psi = new ProcessStartInfo(updater) { UseShellExecute = false, WorkingDirectory = WorkDir };
            psi.ArgumentList.Add("--apply-update");
            psi.ArgumentList.Add(packagePath);
            psi.ArgumentList.Add(self);
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            if (!restart)
                psi.ArgumentList.Add("--no-restart");
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Error(ex);
            return false;
        }
    }

    /// <summary>从更新信息解析出的包名与哈希，判断工作目录里是否已有「已下载并校验通过」的更新包。</summary>
    public static string? PreparedPackage(UpdateInfo info)
    {
        try
        {
            string package = Path.Combine(WorkDir, PackageName);
            if (!File.Exists(package) || info.Sha256.Length != 64)
                return null;
            return Sha256Of(package).Equals(info.Sha256, StringComparison.OrdinalIgnoreCase) ? package : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>更新器主体（`--apply-update &lt;zip&gt; &lt;目标 exe&gt; &lt;旧进程 pid&gt; [--no-restart]`）：
    /// 等旧进程退出 → 解压 → 替换 exe（与包内同名的随包文件，存在才替换）→ 视参数重启。
    /// 「退出时自动安装」用 <paramref name="restart"/>=false：换好即可，下次启动就是新版本（使用中不重启）。</summary>
    public static int ApplyUpdate(string packagePath, string targetExe, int waitPid, bool restart = true)
    {
        try
        {
            Log.Init();
            Log.Info($"更新器启动：package={packagePath} target={targetExe} waitPid={waitPid}");
            WaitForExit(waitPid);

            string extractDir = Path.Combine(WorkDir, "extract");
            TryDeleteDir(extractDir);
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(packagePath, extractDir, overwriteFiles: true);

            string? newExe = FindExe(extractDir);
            if (newExe is null)
            {
                Log.Error("更新包内未找到 Tcursor.exe");
                RelaunchOriginal(targetExe, restart);   // 失败也要把应用还回去
                return 2;
            }

            string targetDir = Path.GetDirectoryName(targetExe)!;
            ReplaceWithRetry(newExe, targetExe);
            ClearPendingManifest();   // 替换已成功：清「已就绪」清单（第一道保险；失败路径保留清单供下次启动重试）

            string sourceDir = Path.GetDirectoryName(newExe)!;
            foreach (string name in new[] { "Tcursor.pdb", "LICENSE", "THIRD-PARTY-NOTICES.txt" })
            {
                string src = Path.Combine(sourceDir, name);
                string dst = Path.Combine(targetDir, name);
                if (File.Exists(src) && File.Exists(dst))
                {
                    try
                    {
                        File.Copy(src, dst, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);   // 随包文件失败不影响主程序
                    }
                }
            }

            if (restart && Environment.GetEnvironmentVariable(NoRestartEnv) is not { Length: > 0 })
            {
                Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
                Log.Info("更新完成，已重启新版本");
            }
            else
            {
                Log.Info("更新完成（未重启：下次启动即为新版本）");
            }

            TryDeleteDir(extractDir);
            TryDelete(packagePath);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            RelaunchOriginal(targetExe, restart);   // 失败也要把应用还回去：否则「启动时安装」失败会表现为程序无声消失
            return 1;
        }
    }

    /// <summary>替换失败时的兜底：调用方（启动时安装 / 手动立即更新）本是「交给更新器后自己退出」，
    /// 若替换没成功就把原 exe 重新拉起来——宁可回到旧版本，也不能让程序无声消失。</summary>
    private static void RelaunchOriginal(string targetExe, bool restart)
    {
        if (!restart || Environment.GetEnvironmentVariable(NoRestartEnv) is { Length: > 0 })
            return;
        try
        {
            Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
            Log.Info("更新失败：已重新启动原版本（应用不会消失）");
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private static void WaitForExit(int pid)
    {
        if (pid <= 0)
            return;
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (!process.WaitForExit((int)ExitWait.TotalMilliseconds))
                Log.Error($"等待旧进程退出超时（pid={pid}），仍继续替换");
        }
        catch (ArgumentException)
        {
            // 进程已不存在：正常路径
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    /// <summary>替换目标 exe：先把新文件复制到**目标同目录的旁路文件**（同卷），校验大小后原子改名覆盖——
    /// 覆盖过程中断电/被杀只会留下 .new 垃圾，不会出现「复制了一半、旧版本又没了」的死局
    /// （原先 File.Copy 原地覆盖：先截断再写，中断即损坏且无法回滚）。运行中的 exe 不能改名，故整体带重试。</summary>
    private static void ReplaceWithRetry(string source, string target)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= 12; attempt++)
        {
            string side = target + ".new";
            try
            {
                File.Copy(source, side, overwrite: true);
                if (new FileInfo(side).Length != new FileInfo(source).Length)
                    throw new IOException("旁路副本大小与源不符（复制不完整）");
                File.Move(side, target, overwrite: true);   // 同卷改名 = 原子元数据操作
                Log.Info($"替换成功：{target}（第 {attempt} 次尝试，旁路+原子改名）");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                TryDelete(side);   // 清掉半成品，下一轮重来
                Thread.Sleep(250);
            }
        }
        throw new IOException($"替换 {target} 失败（重试 12 次）：{last?.Message}", last);
    }

    private static string? FindExe(string root)
    {
        // 包内结构：Tcursor/Tcursor.exe（标准）；兼容任意单层/多层布局
        foreach (string path in Directory.EnumerateFiles(root, "Tcursor.exe", SearchOption.AllDirectories))
            return path;
        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 清理失败无妨（下次更新会覆盖）
        }
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 同上
        }
    }

    // ---------------- 无界面入口（自动化 / 测试） ----------------

    /// <summary>`--check-update &lt;结果文件&gt;`：0 已是最新 / 2 有更新 / 1 检查失败（结果写 JSON 文件）。</summary>
    public static int CheckCli(string outFile)
    {
        UpdateInfo? info = Check(out string error);
        var payload = new Dictionary<string, object?>
        {
            ["current"] = AppInfo.Version,
            ["feed"] = Feed,
            ["error"] = error,
            ["latest"] = info?.Version,
            ["tag"] = info?.Tag,
            ["asset"] = info?.AssetName,
            ["url"] = info?.DownloadUrl,
            ["sha256"] = info?.Sha256,
            ["size"] = info?.Size,
            ["newer"] = info?.Newer ?? false,
            ["canSelfUpdate"] = CanSelfUpdate(out string writeError),
            ["writeError"] = writeError,
        };
        WriteResult(outFile, payload);
        if (info is null)
            return 1;
        return info.Newer ? 2 : 0;
    }

    /// <summary>`--update-now &lt;结果文件&gt;`：检查 → 下载校验 → 启动更新器（0 成功发起 / 1 失败 / 3 已是最新）。</summary>
    public static int UpdateNowCli(string outFile)
    {
        var payload = new Dictionary<string, object?> { ["current"] = AppInfo.Version, ["feed"] = Feed };
        UpdateInfo? info = Check(out string error);
        if (info is null)
        {
            payload["error"] = error;
            WriteResult(outFile, payload);
            return 1;
        }
        payload["latest"] = info.Version;
        payload["asset"] = info.AssetName;
        if (!info.Newer)
        {
            payload["result"] = "已是最新版本";
            WriteResult(outFile, payload);
            return 3;
        }
        string? package = Download(info, null, CancellationToken.None, out string downloadError);
        if (package is null)
        {
            payload["error"] = downloadError;
            WriteResult(outFile, payload);
            return 1;
        }
        payload["package"] = package;
        payload["sha256"] = Sha256Of(package);
        bool spawned = SpawnUpdater(package, out string spawnError);
        payload["spawned"] = spawned;
        payload["error"] = spawnError;
        payload["result"] = spawned ? "更新器已启动" : "更新器启动失败";
        WriteResult(outFile, payload);
        return spawned ? 0 : 1;
    }

    /// <summary>`--prepare-update &lt;结果文件&gt;`：检查 + 静默下载（不安装）。0 已就绪 / 3 已是最新 / 1 失败。</summary>
    public static int PrepareCli(string outFile)
    {
        var payload = new Dictionary<string, object?> { ["current"] = AppInfo.Version, ["feed"] = Feed };
        UpdateInfo? info = Check(out string error);
        if (info is null)
        {
            payload["error"] = error;
            WriteResult(outFile, payload);
            return 1;
        }
        payload["latest"] = info.Version;
        if (!info.Newer)
        {
            payload["result"] = "已是最新版本";
            WriteResult(outFile, payload);
            return 3;
        }
        string? package = Download(info, null, CancellationToken.None, out string downloadError);
        if (package is null)
        {
            payload["error"] = downloadError;
            payload["result"] = "下载失败";
            WriteResult(outFile, payload);
            return 1;
        }
        MarkPrepared(info);
        payload["prepared"] = package;
        payload["sha256"] = Sha256Of(package);
        payload["result"] = "已下载并校验通过（点击立即更新；退出或下次启动时自动安装）";
        WriteResult(outFile, payload);
        return 0;
    }

    /// <summary>`--install-prepared &lt;结果文件&gt;`：安装「已就绪」的更新包（不联网、不重启）。
    /// 0 已交给更新器 / 3 无就绪包 / 1 失败。</summary>
    public static int InstallPreparedCli(string outFile)
    {
        var payload = new Dictionary<string, object?> { ["current"] = AppInfo.Version };
        PreparedInstallResult result = InstallPrepared(out string error, restart: false);
        payload["result"] = result == PreparedInstallResult.Started ? "更新器已启动（未重启：下次启动为新版本）" : error;
        payload["error"] = result == PreparedInstallResult.Started ? "" : error;
        WriteResult(outFile, payload);
        return result switch
        {
            PreparedInstallResult.Started => 0,
            PreparedInstallResult.NothingPrepared => 3,
            _ => 1,
        };
    }

    /// <summary>把「已下载并校验通过」的状态写入清单（退出时安装不联网，只依赖它）。
    /// attempts 记录「已交付更新器但未确认成功」的次数，用于防「更新器反复失败 → 每次启动都重试」的循环。</summary>
    public static void MarkPrepared(UpdateInfo info)
    {
        try
        {
            if (info.Sha256.Length != 64)
                return;   // 语义统一：没有 64 位哈希就不算「已就绪」（与 ReadPrepared 的要求一致）
            Directory.CreateDirectory(WorkDir);
            File.WriteAllText(PendingManifestPath, JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["version"] = info.Version,
                ["sha256"] = info.Sha256,
                ["size"] = info.Size,
                ["attempts"] = 0,
                ["preparedAtUtc"] = DateTime.UtcNow.ToString("O"),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    /// <summary>清单里的交付尝试次数（缺失/解析失败按 0 计）。</summary>
    private static int ReadManifestAttempts()
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(PendingManifestPath));
            return doc.RootElement.TryGetProperty("attempts", out JsonElement e) && e.TryGetInt32(out int n) ? n : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>更新清单里的交付尝试次数（保留版本/哈希等字段）。</summary>
    private static void WriteManifestAttempts(UpdateInfo info, int attempts)
    {
        try
        {
            File.WriteAllText(PendingManifestPath, JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["version"] = info.Version,
                ["sha256"] = info.Sha256,
                ["size"] = info.Size,
                ["attempts"] = attempts,
                ["preparedAtUtc"] = DateTime.UtcNow.ToString("O"),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    /// <summary>读取「已就绪」的更新并**一次性**完成包校验，返回（清单信息, 包路径）。
    /// 上层原先 ReadPrepared→PreparedPackage 会被连续调用三遍，每遍都对 46MB 包做一次全量 sha256。</summary>
    public static (UpdateInfo Info, string Package)? ReadPreparedWithPackage()
    {
        try
        {
            if (!File.Exists(PendingManifestPath))
                return null;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(PendingManifestPath));
            string version = GetString(doc.RootElement, "version");
            string sha = GetString(doc.RootElement, "sha256");
            if (version.Length == 0 || sha.Length != 64)
                return null;
            var info = new UpdateInfo { Tag = "v" + version, Version = version, AssetName = PackageName, Sha256 = sha, Newer = true };
            string? package = PreparedPackage(info);
            return package is null ? null : (info, package);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return null;
        }
    }

    /// <summary>读取「已就绪」的更新（不联网）：清单存在且包哈希相符才返回；否则返回 null。</summary>
    public static UpdateInfo? ReadPrepared() => ReadPreparedWithPackage() is { } prepared ? prepared.Info : null;

    /// <summary>安装「已就绪」的更新包（不联网）：<paramref name="restart"/>=false 用于退出时自动安装。
    /// 调用方按返回的结果分支（勿用错误文案判断语义）。
    /// 清单**不在这里清除**：交给更新器，由它在替换成功后清 —— 替换失败时清单保留（带 attempts 计数），
    /// 下次启动可再试（最多 <see cref="MaxInstallAttempts"/> 次后放弃，避免无限重试）。</summary>
    public static PreparedInstallResult InstallPrepared(out string error, bool restart = false)
    {
        error = "";
        if (!File.Exists(PendingManifestPath))
        {
            error = "没有已下载就绪的更新包";
            return PreparedInstallResult.NothingPrepared;
        }
        if (ReadPreparedWithPackage() is not { } prepared)
        {
            ClearPendingManifest();   // 清单在但包缺失/校验失败：清掉，避免每次启动重复校验
            error = "更新包不完整或校验失败";
            return PreparedInstallResult.Failed;
        }
        int attempts = ReadManifestAttempts();
        if (attempts >= MaxInstallAttempts)
        {
            ClearPendingManifest();
            error = $"更新器连续 {attempts} 次未能完成替换，已放弃本次安装（下次检查会重新下载）";
            Log.Info(error);
            return PreparedInstallResult.Failed;
        }
        WriteManifestAttempts(prepared.Info, attempts + 1);
        if (!SpawnUpdater(prepared.Package, out error, restart))
            return PreparedInstallResult.Failed;
        return PreparedInstallResult.Started;
    }

    /// <summary>清除「已就绪」清单（交接给更新器后调用，避免重复安装）。</summary>
    public static void ClearPendingManifest()
    {
        try
        {
            if (File.Exists(PendingManifestPath))
                File.Delete(PendingManifestPath);
        }
        catch
        {
            // 清理失败无妨（下次校验会发现包缺失/不符）
        }
    }

    /// <summary>启动时安装（v1.3）：若本机已有「已就绪」且**比当前版本更新**的更新包，就交给更新器（带重启）
    /// 并让调用方立即退出 —— 更新器等本进程退出后替换 exe 并启动新版本（约 2 秒）。
    ///
    /// 这补上了两个边界：① 上次被强制结束（任务管理器 / 断电 / 关机超时）没来得及在退出时安装；
    /// ② 上次退出时更新还没下载好。版本比较是防循环的第二道保险（更新器替换后版本已相等，清单也已清除）。</summary>
    public static bool TryInstallPreparedOnStartup(out string note)
    {
        note = "";
        UpdateInfo? info = ReadPrepared();
        if (info is null)
            return false;
        if (CompareVersions(info.Version, AppInfo.Version) <= 0)
        {
            ClearPendingManifest();   // 陈旧清单（版本不新于当前）：清掉，避免每次启动都尝试
            note = $"已就绪清单版本 {info.Version} 不新于当前 {AppInfo.Version}，已清除";
            return false;
        }
        if (InstallPrepared(out string error, restart: true) != PreparedInstallResult.Started)
        {
            note = "启动时安装未进行：" + error;
            return false;
        }
        note = $"已交付更新器：{AppInfo.Version} → {info.Version}（替换后自动启动新版本）";
        return true;
    }

    /// <summary>「已就绪」清单路径</summary>
    public static string PendingManifestPath => Path.Combine(WorkDir, "pending.json");

    /// <summary>「已交付更新器但未确认成功」的最大次数（防失败后每次启动都重试）</summary>
    private const int MaxInstallAttempts = 2;

    private static void WriteResult(string outFile, Dictionary<string, object?> payload)
    {
        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(outFile))!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(outFile, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }
}
