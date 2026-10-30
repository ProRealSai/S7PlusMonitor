using ClosedXML.Excel;
using S7PlusMonitor.Core.Data.Entities;
using S7PlusMonitor.Core.Helpers;
using S7PlusMonitor.Core.Models;

namespace S7PlusMonitor.Core.Export;

/// <summary>变量列表 Excel 导出：ClosedXML 实现，单工作簿双 Sheet（变量 + 说明）。</summary>
public sealed class VariableListExcelExporter : IVariableListExcelExporter
{
    private const string DataSheetName = "变量列表";
    private const string InfoSheetName = "说明";

    private const int TitleRow = 1;
    private const int ConnectionSectionRow = 2;
    private const int ConnectionInfoStartRow = 3;
    private const int ConnectionInfoRowCount = 5;
    private const int BlankRow = ConnectionInfoStartRow + ConnectionInfoRowCount;
    private const int VariableSectionRow = BlankRow + 1;
    private const int GroupHeaderRow = VariableSectionRow + 1;
    private const int ColumnHeaderRow = GroupHeaderRow + 1;
    private const int DataStartRow = ColumnHeaderRow + 1;
    private const int LeftLabelStartCol = 1;
    private const int LeftLabelEndCol = 2;
    private const int LeftValueStartCol = 3;
    private const int LeftValueEndCol = 7;
    private const int RightLabelStartCol = 8;
    private const int RightLabelEndCol = 9;
    private const int RightValueStartCol = 10;
    private const int RightValueEndCol = 14;
    private const int SymbolCrcColumn = 5;
    private const int DataColumnCount = 14;

    private static readonly XLColor TitleBackground = XLColor.FromHtml("#263238");
    private static readonly XLColor SectionBlue = XLColor.FromHtml("#E3F2FD");
    private static readonly XLColor SectionBlueDark = XLColor.FromHtml("#BBDEFB");
    private static readonly XLColor SectionGreen = XLColor.FromHtml("#E8F5E9");
    private static readonly XLColor SectionGreenDark = XLColor.FromHtml("#C8E6C9");
    private static readonly XLColor SectionTintBlue = XLColor.FromHtml("#E1F5FE");
    private static readonly XLColor HeaderBackground = XLColor.FromHtml("#455A64");
    private static readonly XLColor BorderColor = XLColor.FromHtml("#CFD8DC");
    private static readonly XLColor BorderColorDark = XLColor.FromHtml("#90A4AE");
    private static readonly XLColor ZebraBackground = XLColor.FromHtml("#F5F7FA");
    private static readonly XLColor PasswordTint = XLColor.FromHtml("#FFF3E0");
    private static readonly XLColor LabelText = XLColor.FromHtml("#37474F");
    private static readonly XLColor MutedText = XLColor.FromHtml("#607D8B");

    public Task ExportAsync(
        PlcConnectionEntity connection,
        string passwordPlaintext,
        int pollIntervalMs,
        IReadOnlyList<VariableInfo> variables,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var workbook = new XLWorkbook();
        BuildDataSheet(workbook, connection, passwordPlaintext, pollIntervalMs, variables);
        BuildInfoSheet(workbook, variables.Count);

        workbook.SaveAs(outputPath);
        return Task.CompletedTask;
    }

    private static void BuildDataSheet(
        XLWorkbook workbook,
        PlcConnectionEntity connection,
        string passwordPlaintext,
        int pollIntervalMs,
        IReadOnlyList<VariableInfo> variables)
    {
        var sheet = workbook.Worksheets.Add(DataSheetName);
        WriteTitleRow(sheet);
        WriteConnectionSectionHeader(sheet);
        WriteConnectionInfoBlock(sheet, connection, passwordPlaintext, pollIntervalMs);
        WriteVariableSectionHeader(sheet, variables.Count);
        WriteGroupHeaderRow(sheet);
        WriteColumnHeaderRow(sheet);
        WriteVariableRows(sheet, variables);
        ApplySheetStyle(sheet, variables.Count);
    }

    private static void WriteTitleRow(IXLWorksheet sheet)
    {
        sheet.Cell(TitleRow, 1).Value = "S7Plus 监视器 — 变量列表导出";
        sheet.Range(TitleRow, 1, TitleRow, DataColumnCount).Merge();
        var title = sheet.Range(TitleRow, 1, TitleRow, DataColumnCount);
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        title.Style.Font.FontColor = XLColor.White;
        title.Style.Fill.BackgroundColor = TitleBackground;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        title.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        title.Style.Alignment.Indent = 1;
        sheet.Row(TitleRow).Height = 30;
    }

    private static void WriteConnectionSectionHeader(IXLWorksheet sheet)
    {
        sheet.Cell(ConnectionSectionRow, 1).Value = "连接信息";
        sheet.Range(ConnectionSectionRow, 1, ConnectionSectionRow, DataColumnCount).Merge();
        StyleSectionHeader(sheet.Range(ConnectionSectionRow, 1, ConnectionSectionRow, DataColumnCount), SectionBlue, SectionBlueDark);
        sheet.Row(ConnectionSectionRow).Height = 22;
    }

    private static void WriteConnectionInfoBlock(
        IXLWorksheet sheet,
        PlcConnectionEntity connection,
        string passwordPlaintext,
        int pollIntervalMs)
    {
        var exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var pollText = pollIntervalMs == 0 ? "0（连续轮询）" : pollIntervalMs.ToString();
        var securityText = FormatSecurityMode(connection.SecurityMode);

        WriteConnectionFieldPair(
            sheet,
            ConnectionInfoStartRow,
            ("连接名称", connection.Name, false),
            ("IP 地址", connection.Host, false),
            stripe: true);

        WriteConnectionFieldPair(
            sheet,
            ConnectionInfoStartRow + 1,
            ("端口号", connection.Port.ToString(), false),
            ("超时(秒)", connection.TimeoutSeconds.ToString(), false),
            stripe: false);

        WriteConnectionFieldPair(
            sheet,
            ConnectionInfoStartRow + 2,
            ("用户名", connection.Username ?? string.Empty, false),
            ("密码", passwordPlaintext, true),
            stripe: true);

        WriteConnectionFieldPair(
            sheet,
            ConnectionInfoStartRow + 3,
            ("安全模式", securityText, false),
            ("监视周期(ms)", pollText, false),
            stripe: false);

        WriteConnectionFieldFull(
            sheet,
            ConnectionInfoStartRow + 4,
            "导出时间",
            exportedAt,
            stripe: true);

        ApplyBorder(
            sheet.Range(ConnectionInfoStartRow, 1, ConnectionInfoStartRow + ConnectionInfoRowCount - 1, DataColumnCount),
            XLBorderStyleValues.Thin,
            BorderColor);

        sheet.Column(LeftLabelStartCol).Width = 8;
        sheet.Column(LeftLabelEndCol).Width = 8;
        sheet.Column(RightLabelStartCol).Width = 8;
        sheet.Column(RightLabelEndCol).Width = 8;
    }

    private static void WriteConnectionFieldPair(
        IXLWorksheet sheet,
        int row,
        (string Label, string Value, bool IsPassword) left,
        (string Label, string Value, bool IsPassword) right,
        bool stripe)
    {
        WriteConnectionLabelValue(
            sheet,
            row,
            LeftLabelStartCol,
            LeftLabelEndCol,
            LeftValueStartCol,
            LeftValueEndCol,
            left.Label,
            left.Value,
            left.IsPassword,
            stripe);

        WriteConnectionLabelValue(
            sheet,
            row,
            RightLabelStartCol,
            RightLabelEndCol,
            RightValueStartCol,
            RightValueEndCol,
            right.Label,
            right.Value,
            right.IsPassword,
            stripe);

        sheet.Row(row).Height = 20;
    }

    private static void WriteConnectionFieldFull(
        IXLWorksheet sheet,
        int row,
        string label,
        string value,
        bool stripe)
    {
        WriteConnectionLabelValue(
            sheet,
            row,
            LeftLabelStartCol,
            LeftLabelEndCol,
            LeftValueStartCol,
            DataColumnCount,
            label,
            value,
            isPassword: false,
            stripe);

        sheet.Row(row).Height = 20;
    }

    private static void WriteConnectionLabelValue(
        IXLWorksheet sheet,
        int row,
        int labelStartCol,
        int labelEndCol,
        int valueStartCol,
        int valueEndCol,
        string label,
        string value,
        bool isPassword,
        bool stripe)
    {
        var labelCell = sheet.Cell(row, labelStartCol);
        labelCell.Value = isPassword ? "⚠ 密码" : label;
        labelCell.Style.Font.Bold = true;
        labelCell.Style.Font.FontColor = LabelText;
        labelCell.Style.Font.FontSize = 10;
        labelCell.Style.Fill.BackgroundColor = stripe ? SectionBlue : XLColor.White;
        labelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        labelCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        if (labelEndCol > labelStartCol)
        {
            sheet.Range(row, labelStartCol, row, labelEndCol).Merge();
        }

        var valueCell = sheet.Cell(row, valueStartCol);
        valueCell.Value = value;
        valueCell.Style.Font.FontSize = 10;
        valueCell.Style.Font.FontColor = isPassword ? XLColor.FromHtml("#E65100") : LabelText;
        valueCell.Style.Fill.BackgroundColor = isPassword
            ? PasswordTint
            : stripe ? XLColor.FromHtml("#FAFCFF") : XLColor.White;
        valueCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        valueCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        valueCell.Style.Alignment.Indent = 1;
        valueCell.Style.Alignment.WrapText = false;
        valueCell.Style.Alignment.ShrinkToFit = false;

        if (isPassword)
        {
            valueCell.Style.Font.FontName = "Consolas";
            valueCell.Style.NumberFormat.Format = "@";
        }

        if (valueEndCol > valueStartCol)
        {
            sheet.Range(row, valueStartCol, row, valueEndCol).Merge();
        }
    }

    private static void WriteVariableSectionHeader(IXLWorksheet sheet, int variableCount)
    {
        sheet.Cell(VariableSectionRow, 1).Value = $"变量列表（{variableCount} 个）";
        sheet.Range(VariableSectionRow, 1, VariableSectionRow, DataColumnCount).Merge();
        StyleSectionHeader(sheet.Range(VariableSectionRow, 1, VariableSectionRow, DataColumnCount), SectionGreen, SectionGreenDark);
        sheet.Row(VariableSectionRow).Height = 22;
    }

    private static void WriteGroupHeaderRow(IXLWorksheet sheet)
    {
        sheet.Cell(GroupHeaderRow, 1).Value = "变量概要";
        sheet.Range(GroupHeaderRow, 1, GroupHeaderRow, 3).Merge();
        sheet.Cell(GroupHeaderRow, 4).Value = "PLC 符号属性";
        sheet.Range(GroupHeaderRow, 4, GroupHeaderRow, DataColumnCount).Merge();

        var groups = new (XLColor Background, XLColor FontColor, int Start, int End)[]
        {
            (XLColor.FromHtml("#C8E6C9"), XLColor.FromHtml("#1B5E20"), 1, 3),
            (SectionTintBlue, XLColor.FromHtml("#01579B"), 4, DataColumnCount)
        };

        foreach (var (background, fontColor, start, end) in groups)
        {
            var range = sheet.Range(GroupHeaderRow, start, GroupHeaderRow, end);
            range.Style.Fill.BackgroundColor = background;
            range.Style.Font.Bold = true;
            range.Style.Font.FontSize = 10;
            range.Style.Font.FontColor = fontColor;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        sheet.Row(GroupHeaderRow).Height = 20;
    }

    private static void WriteColumnHeaderRow(IXLWorksheet sheet)
    {
        var headers = new[]
        {
            "序号", "变量名称", "数据类型",
            "访问序列", "符号 CRC", "数据类型 ID", "优化地址", "优化位偏移",
            "非优化地址", "非优化位偏移", "HMI 可见", "HMI 可访问", "数组元素数", "数组维度"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(ColumnHeaderRow, i + 1).Value = headers[i];
        }

        var headerRange = sheet.Range(ColumnHeaderRow, 1, ColumnHeaderRow, headers.Length);
        headerRange.Style.Fill.BackgroundColor = HeaderBackground;
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Font.FontSize = 10;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        sheet.Row(ColumnHeaderRow).Height = 32;
    }

    private static void WriteVariableRows(
        IXLWorksheet sheet,
        IReadOnlyList<VariableInfo> variables)
    {
        for (var i = 0; i < variables.Count; i++)
        {
            var row = DataStartRow + i;
            var variable = variables[i];

            sheet.Cell(row, 1).Value = i + 1;
            sheet.Cell(row, 2).Value = variable.Symbol;
            sheet.Cell(row, 3).Value = variable.DataType;
            sheet.Cell(row, 4).Value = variable.Address;
            sheet.Cell(row, 5).Value = VariableDisplayFormatter.FormatHex(variable.SymbolCrc);
            sheet.Cell(row, 6).Value = variable.SoftdatatypeId;
            sheet.Cell(row, 7).Value = VariableDisplayFormatter.FormatHex(variable.OptAddress);
            sheet.Cell(row, 8).Value = variable.OptBitoffset;
            sheet.Cell(row, 9).Value = VariableDisplayFormatter.FormatHex(variable.NonOptAddress);
            sheet.Cell(row, 10).Value = variable.NonOptBitoffset;
            sheet.Cell(row, 11).Value = VariableDisplayFormatter.FormatBool(variable.HmiVisible);
            sheet.Cell(row, 12).Value = VariableDisplayFormatter.FormatBool(variable.HmiAccessible);
            sheet.Cell(row, 13).Value = variable.ArrayElementCount;
            sheet.Cell(row, 14).Value = VariableDisplayFormatter.FormatArrayDimensions(variable.ArrayDimensions);

            sheet.Row(row).Height = 18;
        }
    }

    private static void ApplySheetStyle(IXLWorksheet sheet, int rowCount)
    {
        var lastRow = Math.Max(DataStartRow + rowCount - 1, ColumnHeaderRow);

        ApplyBorder(
            sheet.Range(GroupHeaderRow, 1, lastRow, DataColumnCount),
            XLBorderStyleValues.Thin,
            BorderColor);
        ApplyBorder(
            sheet.Range(GroupHeaderRow, 1, lastRow, DataColumnCount),
            XLBorderStyleValues.Medium,
            BorderColorDark,
            outsideOnly: true);

        if (rowCount > 0)
        {
            for (var row = DataStartRow; row <= lastRow; row++)
            {
                var isZebra = (row - DataStartRow) % 2 == 1;
                var rowRange = sheet.Range(row, 1, row, DataColumnCount);
                if (isZebra)
                {
                    rowRange.Style.Fill.BackgroundColor = ZebraBackground;
                }

                rowRange.Style.Font.FontSize = 10;
                rowRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                sheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                sheet.Cell(row, 1).Style.Font.FontColor = MutedText;

                sheet.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                sheet.Cell(row, 2).Style.Font.Bold = true;
                sheet.Cell(row, 2).Style.Font.FontColor = LabelText;

                sheet.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                sheet.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                sheet.Cell(row, 4).Style.Font.FontName = "Consolas";
                sheet.Cell(row, 4).Style.Font.FontSize = 9;

                var crcCell = sheet.Cell(row, SymbolCrcColumn);
                crcCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                crcCell.Style.Font.FontName = "Consolas";
                crcCell.Style.Font.FontSize = 9;
                crcCell.Style.Font.FontColor = MutedText;

                for (var col = 6; col <= 10; col++)
                {
                    sheet.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(row, col).Style.Font.FontName = "Consolas";
                    sheet.Cell(row, col).Style.Font.FontSize = 9;
                    sheet.Cell(row, col).Style.Font.FontColor = MutedText;
                }

                for (var col = 11; col <= 12; col++)
                {
                    var cell = sheet.Cell(row, col);
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = cell.GetString() == "TRUE"
                        ? XLColor.FromHtml("#2E7D32")
                        : XLColor.FromHtml("#9E9E9E");
                }

                sheet.Cell(row, 13).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                sheet.Cell(row, 14).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            }
        }

        sheet.SheetView.FreezeRows(ColumnHeaderRow);
        sheet.Range(ColumnHeaderRow, 1, ColumnHeaderRow, DataColumnCount).SetAutoFilter();
        sheet.SheetView.ZoomScale = 100;

        sheet.Column(1).Width = 6;
        sheet.Column(2).Width = 28;
        sheet.Column(3).Width = 12;
        sheet.Column(4).Width = 22;
        sheet.Column(SymbolCrcColumn).Width = 28;
        sheet.Columns(6, 10).Width = 12;
        sheet.Columns(11, 12).Width = 10;
        sheet.Column(13).Width = 10;
        sheet.Column(14).Width = 18;

        sheet.Columns(1, DataColumnCount).AdjustToContents(DataStartRow, Math.Min(DataStartRow + 49, lastRow));
        EnforceColumnWidths(sheet);
    }

    private static void EnforceColumnWidths(IXLWorksheet sheet)
    {
        sheet.Column(1).Width = Math.Min(Math.Max(sheet.Column(1).Width, 6), 8);
        sheet.Column(2).Width = Math.Min(Math.Max(sheet.Column(2).Width, 28), 48);
        sheet.Column(3).Width = Math.Min(Math.Max(sheet.Column(3).Width, 12), 20);
        sheet.Column(4).Width = Math.Min(Math.Max(sheet.Column(4).Width, 22), 48);
        sheet.Column(SymbolCrcColumn).Width = Math.Min(Math.Max(sheet.Column(SymbolCrcColumn).Width, 28), 36);

        for (var col = 6; col <= 13; col++)
        {
            sheet.Column(col).Width = Math.Min(Math.Max(sheet.Column(col).Width, 10), 16);
        }

        sheet.Column(14).Width = Math.Min(Math.Max(sheet.Column(14).Width, 18), 36);
    }

    private static void BuildInfoSheet(XLWorkbook workbook, int variableCount)
    {
        var sheet = workbook.Worksheets.Add(InfoSheetName);

        sheet.Cell(1, 1).Value = "S7Plus 监视器 — 变量列表导出";
        sheet.Range(1, 1, 1, 2).Merge();
        sheet.Cell(2, 1).Value = "导出时间";
        sheet.Cell(2, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        sheet.Cell(3, 1).Value = "变量总数";
        sheet.Cell(3, 2).Value = variableCount;
        sheet.Cell(5, 1).Value = "说明";
        sheet.Range(5, 1, 5, 2).Merge();
        sheet.Cell(6, 1).Value = "本 Sheet 为浏览 Tab 自 PLC 读取的全量符号列表，不含监视表配置。";
        sheet.Range(6, 1, 6, 2).Merge();
        sheet.Cell(7, 1).Value = "⚠ 安全提示：「密码」字段为明文存储，请妥善保管，勿通过不受控渠道传播。";
        sheet.Range(7, 1, 7, 2).Merge();

        sheet.Range(1, 1, 1, 2).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, 2).Style.Font.FontSize = 13;
        sheet.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = TitleBackground;
        sheet.Range(1, 1, 1, 2).Style.Font.FontColor = XLColor.White;
        sheet.Range(1, 1, 1, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 28;

        sheet.Range(5, 1, 5, 2).Style.Font.Bold = true;
        sheet.Range(5, 1, 5, 2).Style.Fill.BackgroundColor = SectionBlue;
        sheet.Cell(7, 1).Style.Font.FontColor = XLColor.FromHtml("#D32F2F");

        for (var row = 2; row <= 3; row++)
        {
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 1).Style.Fill.BackgroundColor = SectionBlue;
            sheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            sheet.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#FAFCFF");
        }

        ApplyBorder(sheet.Range(2, 1, 3, 2), XLBorderStyleValues.Thin, BorderColor);
        sheet.Column(1).Width = 14;
        sheet.Column(2).Width = 36;
    }

    private static void StyleSectionHeader(IXLRange range, XLColor background, XLColor borderBottom)
    {
        range.Style.Fill.BackgroundColor = background;
        range.Style.Font.Bold = true;
        range.Style.Font.FontSize = 11;
        range.Style.Font.FontColor = LabelText;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.Indent = 1;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        range.Style.Border.BottomBorderColor = borderBottom;
    }

    private static void ApplyBorder(
        IXLRange range,
        XLBorderStyleValues style,
        XLColor color,
        bool outsideOnly = false)
    {
        if (outsideOnly)
        {
            range.Style.Border.OutsideBorder = style;
            range.Style.Border.OutsideBorderColor = color;
            return;
        }

        range.Style.Border.OutsideBorder = style;
        range.Style.Border.InsideBorder = style;
        range.Style.Border.OutsideBorderColor = color;
        range.Style.Border.InsideBorderColor = color;
    }

    private static string FormatSecurityMode(string? mode) =>
        mode switch
        {
            "Tls" => "Tls（仅 TLS）",
            "LegacyChallenge" => "LegacyChallenge（旧 CPU/未配置 TLS）",
            _ => "Auto（推荐）"
        };
}
