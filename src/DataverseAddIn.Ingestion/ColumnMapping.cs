using System;

namespace DataverseAddIn.Ingestion
{
    /// <summary>Dataverse column type a spreadsheet value is coerced into.</summary>
    public enum SheetValueType
    {
        String = 0,
        Integer = 1,
        Decimal = 2,
        Double = 3,
        Money = 4,
        Boolean = 5,
        DateTime = 6,
        UniqueIdentifier = 7,
        /// <summary>Cell holds the target record's Guid; <see cref="ColumnMapping.LookupTable"/> names the table.</summary>
        Lookup = 8,
        /// <summary>Cell holds the integer option value.</summary>
        OptionSet = 9
    }

    public sealed class ColumnMapping
    {
        public ColumnMapping(int sourceColumnIndex, string targetAttribute, SheetValueType valueType = SheetValueType.String)
        {
            if (sourceColumnIndex < 0) throw new ArgumentOutOfRangeException(nameof(sourceColumnIndex));
            if (string.IsNullOrWhiteSpace(targetAttribute))
                throw new ArgumentException("Target attribute is required.", nameof(targetAttribute));

            SourceColumnIndex = sourceColumnIndex;
            TargetAttribute = targetAttribute;
            ValueType = valueType;
        }

        /// <summary>Zero-based column offset within the sheet block, left to right.</summary>
        public int SourceColumnIndex { get; }

        public string TargetAttribute { get; }

        public SheetValueType ValueType { get; }

        /// <summary>Reject the row when the cell is empty.</summary>
        public bool Required { get; set; }

        /// <summary>Required for <see cref="SheetValueType.Lookup"/>.</summary>
        public string LookupTable { get; set; }
    }
}
