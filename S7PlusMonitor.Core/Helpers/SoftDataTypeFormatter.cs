using System.Reflection;
using S7CommPlusDriver;

namespace S7PlusMonitor.Core.Helpers;

public static class SoftDataTypeFormatter
{
    /// <summary>
    /// TIA 风格显示名；键为去掉 <c>S7COMMP_SOFTDATATYPE_</c> 后的驱动常量名。
    /// </summary>
    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.Ordinal)
    {
        ["ANY"] = "Any",
        ["AOMAID"] = "Aom_Aid",
        ["AOMIDENT"] = "Aom_Ident",
        ["AOMLINK"] = "Aom_Link",
        ["AREF"] = "ARef",
        ["ARRAY"] = "Array",
        ["BBOOL"] = "BBool",
        ["BCD16"] = "Bcd16",
        ["BCD32"] = "Bcd32",
        ["BCD64"] = "Bcd64",
        ["BCD8"] = "Bcd8",
        ["BLOCKCB"] = "Block_Cb",
        ["BLOCKDB"] = "Block_Db",
        ["BLOCKEXT"] = "Block_Ext",
        ["BLOCKFB"] = "Block_Fb",
        ["BLOCKFC"] = "Block_Fc",
        ["BLOCKOB"] = "Block_Ob",
        ["BLOCKSCB"] = "Block_SCb",
        ["BLOCKSDT"] = "Block_SDt",
        ["BLOCKSDB"] = "Block_SDb",
        ["BLOCKSFB"] = "Block_SFb",
        ["BLOCKSFC"] = "Block_SFc",
        ["BLOCKUDT"] = "Block_Udt",
        ["BOOL"] = "Bool",
        ["BYTE"] = "Byte",
        ["CALARM"] = "CAlarm",
        ["CALARM8"] = "CAlarm8",
        ["CALARM8P"] = "CAlarm8P",
        ["CALARMS"] = "CAlarms",
        ["CALARMT"] = "CAlarmT",
        ["CARSEND"] = "CArSend",
        ["CHAR"] = "Char",
        ["CNOTIFY"] = "CNotify",
        ["CNOTIFY8P"] = "CNotify8P",
        ["CONNANY"] = "Conn_Any",
        ["CONNOUC"] = "Conn_Ouc",
        ["CONNPRG"] = "Conn_Prg",
        ["CONNRID"] = "Conn_Rid",
        ["COUNTER"] = "Counter",
        ["CREF"] = "CRef",
        ["DATE"] = "Date",
        ["DATEANDTIME"] = "Date_And_Time",
        ["DBANY"] = "Db_Any",
        ["DBDYN"] = "Db_Dyn",
        ["DBWWW"] = "Db_Www",
        ["DINT"] = "DInt",
        ["DTL"] = "DTL",
        ["DWORD"] = "DWord",
        ["ENDSTRUCT"] = "End_Struct",
        ["ERRORSTRUCT"] = "ErrorStruct",
        ["EVENTANY"] = "Event_Any",
        ["EVENTATT"] = "Event_Att",
        ["EVENTHWINT"] = "Event_HwInt",
        ["FBTREF"] = "FbtRef",
        ["FOLDER"] = "Folder",
        ["HWANY"] = "Hw_Any",
        ["HWDEVICE"] = "Hw_Device",
        ["HWDPMASTER"] = "Hw_DpMaster",
        ["HWDPSLAVE"] = "Hw_DpSlave",
        ["HWHSC"] = "Hw_Hsc",
        ["HWIEPORT"] = "Hw_IePort",
        ["HWINTERFACE"] = "Hw_Interface",
        ["HWIO"] = "Hw_Io",
        ["HWIOSYSTEM"] = "Hw_IoSystem",
        ["HWMODULE"] = "Hw_Module",
        ["HWNR"] = "Hw_Nr",
        ["HWPTO"] = "Hw_Pto",
        ["HWPWM"] = "Hw_Pwm",
        ["HWSUBMODULE"] = "Hw_SubModule",
        ["IECDCOUNTER"] = "IecDCounter",
        ["IECLCOUNTER"] = "IecLCounter",
        ["IECLTIMER"] = "IecLTimer",
        ["IECCOUNTER"] = "IecCounter",
        ["IECSCOUNTER"] = "IecSCounter",
        ["IECTIMER"] = "IecTimer",
        ["IECUDCOUNTER"] = "IecUDCounter",
        ["IECUCOUNTER"] = "IecUCounter",
        ["IECULCOUNTER"] = "IecULCounter",
        ["IECUSCOUNTER"] = "IecUSCounter",
        ["INT"] = "Int",
        ["INTERNETTIME"] = "InternetTime",
        ["LABEL"] = "Label",
        ["LDT"] = "LDate_And_Time",
        ["LINT"] = "LInt",
        ["LREAL"] = "LReal",
        ["LTIME"] = "LTime",
        ["LTOD"] = "LTime_Of_Day",
        ["LWORD"] = "LWord",
        ["MULTIFB"] = "Multi_Fb",
        ["MULTISFB"] = "Multi_Sfb",
        ["NOTCHOSEN"] = "Not_Chosen",
        ["NREF"] = "NRef",
        ["OBANY"] = "Ob_Any",
        ["OBATT"] = "Ob_Att",
        ["OBCOMM"] = "Ob_Comm",
        ["OBCYCLIC"] = "Ob_Cyclic",
        ["OBDELAY"] = "Ob_Delay",
        ["OBDIAG"] = "Ob_Diag",
        ["OBHWINT"] = "Ob_HwInt",
        ["OBPCYCLE"] = "Ob_PCycle",
        ["OBSTARTUP"] = "Ob_Startup",
        ["OBTIMEERROR"] = "Ob_TimeError",
        ["OBTOD"] = "Ob_Tod",
        ["OFFSET"] = "Offset",
        ["OPCUALOCTXTENCM"] = "OpcUa_LocTxtEncM",
        ["OPCUASTRACTLEN"] = "OpcUa_StrActLen",
        ["PARA"] = "Para",
        ["PIP"] = "Pip",
        ["POINTER"] = "Pointer",
        ["PORT"] = "Port",
        ["REAL"] = "Real",
        ["REMOTE"] = "Remote",
        ["RTM"] = "Rtm",
        ["S5COUNT"] = "S5Count",
        ["S5TIME"] = "S5Time",
        ["SINT"] = "SInt",
        ["STRING"] = "String",
        ["STRUCT"] = "Struct",
        ["TIME"] = "Time",
        ["TIMEOFDAY"] = "Time_Of_Day",
        ["TIMER"] = "Timer",
        ["UDEFINED"] = "Undefined",
        ["UDINT"] = "UDInt",
        ["UINT"] = "UInt",
        ["ULINT"] = "ULInt",
        ["USINT"] = "USInt",
        ["VAREF"] = "VarRef",
        ["VARIANT"] = "Variant",
        ["VOID"] = "Void",
        ["VREF"] = "VRef",
        ["WCHAR"] = "WChar",
        ["WORD"] = "Word",
        ["WSTRING"] = "WString"
    };

    private static readonly IReadOnlyDictionary<uint, string> Names = BuildMap();

    public static string ToEnglishName(uint softdatatypeId) =>
        Names.TryGetValue(softdatatypeId, out var name)
            ? name
            : $"Unknown ({softdatatypeId})";

    private static Dictionary<uint, string> BuildMap()
    {
        var map = new Dictionary<uint, string>();

        foreach (var field in typeof(Softdatatype).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(uint))
            {
                continue;
            }

            var value = (uint)field.GetValue(null)!;
            map[value] = FormatConstantName(field.Name);
        }

        return map;
    }

    private static string FormatConstantName(string constantName)
    {
        var name = constantName;
        if (name.StartsWith("S7COMMP_SOFTDATATYPE_", StringComparison.Ordinal))
        {
            name = name["S7COMMP_SOFTDATATYPE_".Length..];
        }

        return KnownNames.TryGetValue(name, out var englishName)
            ? englishName
            : name;
    }
}
