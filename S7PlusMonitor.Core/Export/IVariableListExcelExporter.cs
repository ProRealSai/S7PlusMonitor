using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Export;

/// <summary>变量列表 Excel 导出抽象（ClosedXML 实现）。</summary>
public interface IVariableListExcelExporter
{
    Task ExportAsync(
        PlcConnectionEntity connection,
        string passwordPlaintext,
        int pollIntervalMs,
        IReadOnlyList<VariableInfo> variables,
        string outputPath,
        CancellationToken cancellationToken = default);
}
