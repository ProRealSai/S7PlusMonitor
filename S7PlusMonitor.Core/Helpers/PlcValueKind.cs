namespace S7PlusMonitor.Core.Helpers;

/// <summary>PLC 值种类（顶层，供 DisplayFormat 与解析共用；原嵌套于 DisplayFormatRegistry）。</summary>
public enum PlcValueKind
{
    Bool,
    Byte,
    Word,
    DWord,
    LWord,
    SignedInteger,
    UnsignedInteger,
    Float,
    Double,
    String,
    Unknown
}
