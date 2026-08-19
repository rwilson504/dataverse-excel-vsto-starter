using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace DataverseAddIn.Ingestion
{
    public sealed class SheetRowError
    {
        public SheetRowError(int rowNumber, string column, string message)
        {
            RowNumber = rowNumber;
            Column = column;
            Message = message;
        }

        /// <summary>1-based worksheet row, so it can be shown to the user as-is.</summary>
        public int RowNumber { get; }

        public string Column { get; }

        public string Message { get; }

        public override string ToString() => $"Row {RowNumber}, {Column}: {Message}";
    }

    public sealed class SheetMappingResult
    {
        internal SheetMappingResult(IReadOnlyList<Entity> records, IReadOnlyList<int> sourceRowNumbers, IReadOnlyList<SheetRowError> errors)
        {
            Records = records;
            SourceRowNumbers = sourceRowNumbers;
            Errors = errors;
        }

        public IReadOnlyList<Entity> Records { get; }

        /// <summary>Worksheet row number for each entry in <see cref="Records"/>, parallel by index.</summary>
        public IReadOnlyList<int> SourceRowNumbers { get; }

        public IReadOnlyList<SheetRowError> Errors { get; }
    }

    /// <summary>
    /// Converts a block of worksheet cells into <see cref="Entity"/> records.
    /// </summary>
    /// <remarks>
    /// Takes <c>object[,]</c> — exactly what Excel's <c>Range.Value2</c> hands back — so it has
    /// no Office dependency and can be unit tested. Note that Excel returns a <b>1-based</b>
    /// array, so bounds are read rather than assumed.
    /// <para>
    /// Rows that fail validation are reported instead of being sent, which matters because a
    /// bulk request on a standard table rolls back entirely on the first bad record.
    /// </para>
    /// </remarks>
    public sealed class SheetMapper
    {
        private readonly string _tableLogicalName;
        private readonly IReadOnlyList<ColumnMapping> _mappings;

        public SheetMapper(string tableLogicalName, IReadOnlyList<ColumnMapping> mappings)
        {
            if (string.IsNullOrWhiteSpace(tableLogicalName))
                throw new ArgumentException("Table logical name is required.", nameof(tableLogicalName));
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));
            if (mappings.Count == 0)
                throw new ArgumentException("At least one column mapping is required.", nameof(mappings));

            _tableLogicalName = tableLogicalName;
            _mappings = mappings;
        }

        /// <param name="values">Cell block, typically straight from <c>Range.Value2</c>.</param>
        /// <param name="skipFirstRow">True when the block includes the header row.</param>
        /// <param name="firstRowNumber">Worksheet row number of the block's first row, for error reporting.</param>
        public SheetMappingResult Map(object[,] values, bool skipFirstRow = true, int firstRowNumber = 1)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));

            var rowLower = values.GetLowerBound(0);
            var rowUpper = values.GetUpperBound(0);
            var colLower = values.GetLowerBound(1);
            var colUpper = values.GetUpperBound(1);

            var records = new List<Entity>();
            var rowNumbers = new List<int>();
            var errors = new List<SheetRowError>();

            var firstDataRow = skipFirstRow ? rowLower + 1 : rowLower;

            for (var row = firstDataRow; row <= rowUpper; row++)
            {
                var worksheetRow = firstRowNumber + (row - rowLower);

                // A wholly blank row is trailing selection, not data. Skip before validating,
                // otherwise every required column reports an error for every empty row.
                if (IsRowBlank(values, row, colLower, colUpper)) continue;

                var entity = new Entity(_tableLogicalName);
                var rowHadError = false;

                foreach (var mapping in _mappings)
                {
                    var column = colLower + mapping.SourceColumnIndex;

                    if (column > colUpper)
                    {
                        errors.Add(new SheetRowError(worksheetRow, mapping.TargetAttribute,
                            $"Column offset {mapping.SourceColumnIndex} is outside the selected range."));
                        rowHadError = true;
                        break;
                    }

                    var raw = values[row, column];

                    if (IsEmpty(raw))
                    {
                        if (mapping.Required)
                        {
                            errors.Add(new SheetRowError(worksheetRow, mapping.TargetAttribute, "Value is required."));
                            rowHadError = true;
                        }

                        continue;
                    }

                    try
                    {
                        entity[mapping.TargetAttribute] = Convert(raw, mapping);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new SheetRowError(worksheetRow, mapping.TargetAttribute, ex.Message));
                        rowHadError = true;
                    }
                }

                if (rowHadError) continue;

                records.Add(entity);
                rowNumbers.Add(worksheetRow);
            }

            return new SheetMappingResult(records, rowNumbers, errors);
        }

        private bool IsRowBlank(object[,] values, int row, int colLower, int colUpper)
        {
            foreach (var mapping in _mappings)
            {
                var column = colLower + mapping.SourceColumnIndex;

                if (column <= colUpper && !IsEmpty(values[row, column]))
                    return false;
            }

            return true;
        }

        private static bool IsEmpty(object value) =>
            value == null || (value is string text && text.Trim().Length == 0);

        private static object Convert(object raw, ColumnMapping mapping)
        {
            var text = raw as string;

            switch (mapping.ValueType)
            {
                case SheetValueType.String:
                    return raw.ToString();

                case SheetValueType.Integer:
                    return ToInt(raw, text);

                case SheetValueType.OptionSet:
                    return new OptionSetValue(ToInt(raw, text));

                case SheetValueType.Decimal:
                    return ToDecimal(raw, text);

                case SheetValueType.Money:
                    return new Money(ToDecimal(raw, text));

                case SheetValueType.Double:
                    return raw is double d
                        ? d
                        : double.TryParse(text ?? raw.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var parsedDouble)
                            ? parsedDouble
                            : throw new FormatException($"'{raw}' is not a number.");

                case SheetValueType.Boolean:
                    return ToBoolean(raw, text);

                case SheetValueType.DateTime:
                    return ToDateTime(raw, text);

                case SheetValueType.UniqueIdentifier:
                    return ToGuid(raw);

                case SheetValueType.Lookup:
                    if (string.IsNullOrWhiteSpace(mapping.LookupTable))
                        throw new InvalidOperationException($"{mapping.TargetAttribute} is a lookup but no LookupTable was set.");

                    return new EntityReference(mapping.LookupTable, ToGuid(raw));

                default:
                    throw new ArgumentOutOfRangeException(nameof(mapping));
            }
        }

        private static int ToInt(object raw, string text)
        {
            // Excel hands back every number as double, including whole ones.
            if (raw is double d)
            {
                if (Math.Abs(d % 1) > double.Epsilon)
                    throw new FormatException($"'{raw}' is not a whole number.");

                return checked((int)d);
            }

            return int.TryParse(text ?? raw.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed)
                ? parsed
                : throw new FormatException($"'{raw}' is not a whole number.");
        }

        private static decimal ToDecimal(object raw, string text)
        {
            if (raw is double d) return (decimal)d;

            return decimal.TryParse(text ?? raw.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed)
                ? parsed
                : throw new FormatException($"'{raw}' is not a number.");
        }

        private static bool ToBoolean(object raw, string text)
        {
            if (raw is bool b) return b;
            if (raw is double d) return Math.Abs(d) > double.Epsilon;

            var value = (text ?? raw.ToString()).Trim();

            if (bool.TryParse(value, out var parsed)) return parsed;

            switch (value.ToUpperInvariant())
            {
                case "Y":
                case "YES":
                case "1":
                    return true;
                case "N":
                case "NO":
                case "0":
                    return false;
                default:
                    throw new FormatException($"'{raw}' is not a yes/no value.");
            }
        }

        private static DateTime ToDateTime(object raw, string text)
        {
            if (raw is DateTime dt) return dt;

            // Value2 returns dates as an OLE Automation serial number.
            if (raw is double serial) return DateTime.FromOADate(serial);

            return DateTime.TryParse(text ?? raw.ToString(), CultureInfo.CurrentCulture,
                DateTimeStyles.None, out var parsed)
                ? parsed
                : throw new FormatException($"'{raw}' is not a date.");
        }

        private static Guid ToGuid(object raw) =>
            Guid.TryParse(raw.ToString().Trim(), out var parsed)
                ? parsed
                : throw new FormatException($"'{raw}' is not a GUID.");
    }
}
