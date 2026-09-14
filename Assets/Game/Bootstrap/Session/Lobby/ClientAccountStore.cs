using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace RaidDemo.Bootstrap
{
    /// <summary>客户端本地保存的联机身份信息。</summary>
    [Serializable]
    public sealed class ClientAccountFile
    {
        /// <summary>上次登录成功的昵称。</summary>
        public string Nickname = string.Empty;

        /// <summary>服务器下发的自动登录 token。</summary>
        public string Token = string.Empty;

        /// <summary>上次连接的地址（下次进联机界面时预先填好）。</summary>
        public string LastAddress = string.Empty;
    }

    /// <summary>
    /// 联机身份在客户端的本地存储：昵称 + token + 上次地址。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么存在 <see cref="Application.persistentDataPath"/>：</b>这是 Unity 认可的
    /// "每个用户自己的可写目录"，在 Windows 上落在用户目录下，不进仓库也不进安装目录——
    /// 工程规范禁止把任何本机绝对路径写进源码，而运行时路径必须由引擎决定。</para>
    ///
    /// <para><b>token 的安全边界：</b>它是"这台机器登录过"的凭据，明文存在本地。
    /// 这与主流客户端的做法一致（本地凭据可被同机进程读取），真正的兜底是服务端可以换发 token——
    /// P4 阶段还没有跨局资产，token 泄露的后果只是"别人能上你的号进大厅"。</para>
    ///
    /// <para>读写失败一律不抛异常：本地文件问题不该让玩家连联机界面都进不去。</para>
    /// </remarks>
    public static class ClientAccountStore
    {
        /// <summary>本地文件名。</summary>
        public const string FileName = "multiplayer_account.json";

        /// <summary>本地文件路径。</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>
        /// 读取本地身份信息。
        /// </summary>
        /// <param name="data">读到的数据；文件不存在或损坏时为 null。</param>
        public static bool TryLoad(out ClientAccountFile data)
        {
            data = null;

            try
            {
                var path = FilePath;
                if (!File.Exists(path))
                {
                    return false;
                }

                var json = File.ReadAllText(path, Encoding.UTF8);
                data = JsonUtility.FromJson<ClientAccountFile>(json);
                return data != null;
            }
            catch (Exception)
            {
                // 损坏的本地文件按"没有凭据"处理：玩家重新输一次口令即可，不需要报错打断流程。
                data = null;
                return false;
            }
        }

        /// <summary>
        /// 取指定昵称的 token。
        /// </summary>
        /// <param name="nickname">要登录的昵称。</param>
        /// <param name="token">该昵称对应的 token。</param>
        /// <remarks>只有在昵称与本地记录一致时才返回 token：换了昵称就该按新昵称走完整流程。</remarks>
        public static bool TryGetToken(string nickname, out string token)
        {
            token = null;

            if (!TryLoad(out var data) || string.IsNullOrEmpty(data.Token))
            {
                return false;
            }

            if (!string.Equals(data.Nickname, (nickname ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            token = data.Token;
            return true;
        }

        /// <summary>保存身份信息（登录成功后调用）。</summary>
        /// <param name="nickname">昵称。</param>
        /// <param name="token">服务器下发的 token。</param>
        /// <param name="address">本次连接的地址。</param>
        public static void Save(string nickname, string token, string address)
        {
            try
            {
                var data = new ClientAccountFile
                {
                    Nickname = nickname ?? string.Empty,
                    Token = token ?? string.Empty,
                    LastAddress = address ?? string.Empty,
                };

                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[联机] 本地身份保存失败：{exception.Message}");
            }
        }

        /// <summary>清空本地身份（换账号时用）。</summary>
        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[联机] 本地身份清除失败：{exception.Message}");
            }
        }
    }
}
