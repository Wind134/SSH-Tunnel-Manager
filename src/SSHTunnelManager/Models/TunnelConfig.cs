namespace SSHTunnelManager.Models;

public enum AuthMethod { Password, PrivateKey }
public enum HostKeyTrust { Unknown, Trusted, Rejected }

public class TunnelConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int LocalPort { get; set; } = 7890;
    public int RemotePort { get; set; } = 1080;
    public string SshUser { get; set; } = "root";
    public string EncryptedHost { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public AuthMethod AuthMethod { get; set; } = AuthMethod.Password;
    public string EncryptedPassword { get; set; } = string.Empty;
    public string KeyFilePath { get; set; } = string.Empty;
    public string HostKeyFingerprint { get; set; } = string.Empty;
    public HostKeyTrust HostKeyTrust { get; set; } = HostKeyTrust.Unknown;
    public bool AutoReconnect { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    public TunnelConfig Clone()
    {
        return new TunnelConfig
        {
            Id = Id,
            Name = Name,
            LocalPort = LocalPort,
            RemotePort = RemotePort,
            SshUser = SshUser,
            EncryptedHost = EncryptedHost,
            SshPort = SshPort,
            AuthMethod = AuthMethod,
            EncryptedPassword = EncryptedPassword,
            KeyFilePath = KeyFilePath,
            HostKeyFingerprint = HostKeyFingerprint,
            HostKeyTrust = HostKeyTrust,
            AutoReconnect = AutoReconnect,
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt
        };
    }
}
