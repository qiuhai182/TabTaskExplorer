using System.Text.Json;
using TabTaskExplorer.Kernel.Abstractions;
using TabTaskExplorer.Kernel.Models;

namespace TabTaskExplorer.Kernel.Services;

/// <summary>
/// 远程连接管理器：连接配置的增删改查、凭据加解密、连通性测试与导出导入。
/// 应用层只使用本类，不直接接触存储与凭据保护细节。
/// </summary>
public class ConnectionManager
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    private readonly IConnectionStore store;
    private readonly ISecretProtector protector;
    private readonly IConnectivityTester tester;
    private readonly Action<string>? log;

    /// <summary>使用指定存储、凭据保护器与连通性测试器创建管理器。</summary>
    public ConnectionManager(IConnectionStore store, ISecretProtector protector, IConnectivityTester tester,
        Action<string>? log = null)
    {
        this.store = store;
        this.protector = protector;
        this.tester = tester;
        this.log = log;
        this.MigrateLegacySecrets();
    }

    /// <summary>
    /// 一次性迁移旧凭据：无版本前缀的历史值先解密再以新格式加密写回，迁移前备份原文件。
    /// 任一凭据迁移失败则不写入任何内容，保留可回滚原件并通过日志回调说明原因。
    /// </summary>
    private void MigrateLegacySecrets()
    {
        List<RemoteConnectionProfile> all;
        try
        {
            all = new List<RemoteConnectionProfile>(this.store.LoadAll());
        }
        catch (Exception error)
        {
            this.Log("读取连接配置失败，跳过凭据迁移：原因=" + error.Message);
            return;
        }

        int migratedCount = 0;
        List<string> failedNames = new List<string>();
        foreach (RemoteConnectionProfile profile in all)
        {
            if (profile == null || string.IsNullOrEmpty(profile.ProtectedSecret))
            {
                continue;
            }
            if (profile.ProtectedSecret.StartsWith("dpapi:v2:", StringComparison.Ordinal)
                || profile.ProtectedSecret.StartsWith("aesgcm:v1:", StringComparison.Ordinal))
            {
                continue;
            }
            try
            {
                string plaintext = this.protector.Unprotect(profile.ProtectedSecret);
                string protectedValue = this.protector.Protect(plaintext);
                if (!string.IsNullOrEmpty(protectedValue) && protectedValue != profile.ProtectedSecret)
                {
                    profile.ProtectedSecret = protectedValue;
                    migratedCount++;
                }
            }
            catch (Exception error)
            {
                failedNames.Add(profile.Name + "（" + error.GetType().Name + "）");
            }
        }

        if (failedNames.Count > 0)
        {
            this.Log("凭据迁移中止：存在无法解密的旧凭据，原文件保持不变，请重新输入凭据。连接="
                + string.Join("、", failedNames));
            return;
        }
        if (migratedCount == 0)
        {
            return;
        }

        string backupPath = string.Empty;
        try
        {
            string storePath = this.store.StorePath;
            if (!string.IsNullOrEmpty(storePath) && File.Exists(storePath))
            {
                backupPath = storePath + ".bak-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(storePath, backupPath, false);
            }
            this.store.SaveAll(all);
        }
        catch (Exception error)
        {
            this.Log("凭据迁移写入失败：已保留原文件与备份（备份=" + backupPath + "），原因=" + error.Message);
            return;
        }
        this.Log("凭据迁移完成：迁移条数=" + migratedCount + " 备份文件=" + backupPath
            + " 新格式=平台保护器版本前缀");
    }

    private void Log(string message)
    {
        Action<string>? handler = this.log;
        if (handler != null)
        {
            handler(message);
        }
    }

    /// <summary>读取全部连接配置。</summary>
    public IReadOnlyList<RemoteConnectionProfile> GetAll()
    {
        return this.store.LoadAll();
    }

    /// <summary>按编号查找连接配置；找不到时返回 null。</summary>
    public RemoteConnectionProfile? FindById(string id)
    {
        foreach (RemoteConnectionProfile profile in this.store.LoadAll())
        {
            if (profile.Id == id)
            {
                return profile;
            }
        }
        return null;
    }

    /// <summary>新增连接配置：分配编号与时间戳后保存。</summary>
    public RemoteConnectionProfile Add(RemoteConnectionProfile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }
        profile.Id = Guid.NewGuid().ToString("N");
        DateTime now = DateTime.Now;
        profile.CreatedTime = now;
        profile.ModifiedTime = now;

        List<RemoteConnectionProfile> all = new List<RemoteConnectionProfile>(this.store.LoadAll());
        all.Add(profile);
        this.store.SaveAll(all);
        return profile;
    }

    /// <summary>更新连接配置：按编号替换并刷新修改时间；编号不存在时返回 false。</summary>
    public bool Update(RemoteConnectionProfile profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }
        List<RemoteConnectionProfile> all = new List<RemoteConnectionProfile>(this.store.LoadAll());
        for (int index = 0; index < all.Count; index++)
        {
            if (all[index].Id == profile.Id)
            {
                profile.ModifiedTime = DateTime.Now;
                all[index] = profile;
                this.store.SaveAll(all);
                return true;
            }
        }
        return false;
    }

    /// <summary>按编号删除连接配置；删除成功返回 true。</summary>
    public bool Delete(string id)
    {
        List<RemoteConnectionProfile> all = new List<RemoteConnectionProfile>(this.store.LoadAll());
        for (int index = 0; index < all.Count; index++)
        {
            if (all[index].Id == id)
            {
                all.RemoveAt(index);
                this.store.SaveAll(all);
                return true;
            }
        }
        return false;
    }

    /// <summary>保存明文凭据：先加密再写入指定连接配置。</summary>
    public void SetSecret(RemoteConnectionProfile profile, string plaintextSecret)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }
        profile.ProtectedSecret = this.protector.Protect(plaintextSecret ?? string.Empty);
        this.Update(profile);
    }

    /// <summary>读取明文凭据：取存储密文并解密。</summary>
    public string GetSecret(RemoteConnectionProfile profile)
    {
        if (profile == null || string.IsNullOrEmpty(profile.ProtectedSecret))
        {
            return string.Empty;
        }
        return this.protector.Unprotect(profile.ProtectedSecret);
    }

    /// <summary>测试连接可达性。</summary>
    public ConnectionTestResult Test(RemoteConnectionProfile profile, int timeoutMilliseconds)
    {
        return this.tester.Test(profile, timeoutMilliseconds);
    }

    /// <summary>把全部连接配置导出为 JSON 文件。密文原样导出，跨机器导入后可能需要重新输入凭据。</summary>
    public void ExportTo(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        string json = JsonSerializer.Serialize(this.store.LoadAll(), jsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// 一键导入本机已安装的 Xshell / Xftp 会话配置。
    /// 按连接名称合并：同名连接只更新连接参数并保留原有编号、密码、私钥路径与创建时间。
    /// </summary>
    /// <param name="homeDirectory">用于定位配置目录的用户主目录；为空时取当前登录用户的主目录。</param>
    public XshellImportSummary ImportInstalledXshellSessions(string? homeDirectory = null)
    {
        XshellImportSummary summary = new XshellImportSummary();
        XshellDiscoveryResult discovery = XshellSessionImporter.DiscoverInstalledSessions(homeDirectory);
        summary.RootsScanned.AddRange(discovery.RootsScanned);
        summary.Skipped.AddRange(discovery.Skipped);
        if (discovery.Sessions.Count == 0)
        {
            return summary;
        }

        List<RemoteConnectionProfile> all = new List<RemoteConnectionProfile>(this.store.LoadAll());
        int importedCount = 0;
        int updatedCount = 0;
        foreach (XshellSessionEntry entry in discovery.Sessions)
        {
            RemoteConnectionProfile? source = entry.Profile;
            if (source == null)
            {
                continue;
            }

            int existingIndex = -1;
            for (int index = 0; index < all.Count; index++)
            {
                if (all[index].Name == source.Name)
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                DateTime now = DateTime.Now;
                source.Id = Guid.NewGuid().ToString("N");
                source.CreatedTime = now;
                source.ModifiedTime = now;
                all.Add(source);
                importedCount++;
            }
            else
            {
                RemoteConnectionProfile existing = all[existingIndex];
                existing.Group = source.Group;
                existing.Protocol = source.Protocol;
                existing.Host = source.Host;
                existing.Port = source.Port;
                existing.Username = source.Username;
                existing.AuthType = source.AuthType;
                existing.ModifiedTime = DateTime.Now;
                updatedCount++;
            }
            summary.Names.Add(source.Name);
        }

        if (importedCount > 0 || updatedCount > 0)
        {
            this.store.SaveAll(all);
        }
        summary.Imported = importedCount;
        summary.Updated = updatedCount;
        return summary;
    }

    /// <summary>从 JSON 文件导入连接配置，编号已存在的条目跳过。</summary>
    /// <returns>实际导入的条目数量。</returns>
    public int ImportFrom(string filePath)
    {
        string json = File.ReadAllText(filePath);
        List<RemoteConnectionProfile>? incoming = JsonSerializer.Deserialize<List<RemoteConnectionProfile>>(json, jsonOptions);
        if (incoming == null)
        {
            return 0;
        }

        List<RemoteConnectionProfile> all = new List<RemoteConnectionProfile>(this.store.LoadAll());
        int importedCount = 0;
        foreach (RemoteConnectionProfile profile in incoming)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Id))
            {
                continue;
            }
            bool exists = false;
            foreach (RemoteConnectionProfile existing in all)
            {
                if (existing.Id == profile.Id)
                {
                    exists = true;
                    break;
                }
            }
            if (exists)
            {
                continue;
            }
            all.Add(profile);
            importedCount++;
        }

        if (importedCount > 0)
        {
            this.store.SaveAll(all);
        }
        return importedCount;
    }
}
