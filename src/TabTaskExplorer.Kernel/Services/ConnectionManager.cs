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

    /// <summary>使用指定存储、凭据保护器与连通性测试器创建管理器。</summary>
    public ConnectionManager(IConnectionStore store, ISecretProtector protector, IConnectivityTester tester)
    {
        this.store = store;
        this.protector = protector;
        this.tester = tester;
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
