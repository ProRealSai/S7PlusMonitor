using System.ComponentModel;
using SQLite;

namespace S7PlusMonitor.Core.Data.Entities;

/// <summary>PLC 连接档案实体（plc_connections 表，密码存 SecureStorage）。</summary>

[Table("plc_connections")]
public sealed class PlcConnectionEntity : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    [PrimaryKey, AutoIncrement, Column("id")]
    public int Id { get; set; }

    [Unique, NotNull, Column("name")]
    public string Name
    {
        get => _name;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_name, next, StringComparison.Ordinal))
            {
                return;
            }

            _name = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public override string ToString() => Name;

    [NotNull, Column("host")]
    public string Host { get; set; } = string.Empty;

    [Column("port")]
    public int Port { get; set; } = 102;

    [Column("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 10;

    [Column("username")]
    public string? Username { get; set; }

    [NotNull, Column("security_mode")]
    public string SecurityMode { get; set; } = "Auto";

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("last_used_at")]
    public string? LastUsedAt { get; set; }

    [NotNull, Column("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [NotNull, Column("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;
}

[Table("app_settings")]
public sealed class AppSettingEntity
{
    [PrimaryKey, NotNull]
    public string Key { get; set; } = string.Empty;

    [NotNull]
    public string Value { get; set; } = string.Empty;
}
