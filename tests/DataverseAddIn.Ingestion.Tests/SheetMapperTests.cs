using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace DataverseAddIn.Ingestion.Tests
{
    public class SheetMapperTests
    {
        private const string Table = "sample_widget";

        /// <summary>Excel's Range.Value2 hands back a 1-based array, so tests use one too.</summary>
        private static object[,] Sheet(params object[][] rows)
        {
            var block = (object[,])Array.CreateInstance(
                typeof(object),
                new[] { rows.Length, rows[0].Length },
                new[] { 1, 1 });

            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    block[r + 1, c + 1] = rows[r][c];

            return block;
        }

        [Fact]
        public void Maps_a_one_based_block_and_skips_the_header()
        {
            var sheet = Sheet(
                new object[] { "Name", "Count" },
                new object[] { "first", 1d },
                new object[] { "second", 2d });

            var mapper = new SheetMapper(Table, new[]
            {
                new ColumnMapping(0, "sample_name"),
                new ColumnMapping(1, "sample_count", SheetValueType.Integer)
            });

            var result = mapper.Map(sheet);

            Assert.Empty(result.Errors);
            Assert.Equal(2, result.Records.Count);
            Assert.Equal("first", result.Records[0]["sample_name"]);
            Assert.Equal(1, result.Records[0]["sample_count"]);
            // Header is row 1, so data starts at worksheet row 2.
            Assert.Equal(new[] { 2, 3 }, result.SourceRowNumbers);
        }

        [Fact]
        public void Coerces_the_types_Excel_actually_returns()
        {
            var sheet = Sheet(
                new object[] { "h1", "h2", "h3", "h4", "h5" },
                new object[] { 42d, 19.99d, true, new DateTime(2026, 8, 18).ToOADate(), "3f56361a-b210-4a74-8708-3c664038fa41" });

            var mapper = new SheetMapper(Table, new[]
            {
                new ColumnMapping(0, "sample_count", SheetValueType.Integer),
                new ColumnMapping(1, "sample_price", SheetValueType.Money),
                new ColumnMapping(2, "sample_active", SheetValueType.Boolean),
                new ColumnMapping(3, "sample_when", SheetValueType.DateTime),
                new ColumnMapping(4, "sample_parentid", SheetValueType.Lookup) { LookupTable = "sample_parent" }
            });

            var record = Assert.Single(mapper.Map(sheet).Records);

            Assert.Equal(42, record["sample_count"]);
            Assert.Equal(19.99m, ((Money)record["sample_price"]).Value);
            Assert.True((bool)record["sample_active"]);
            Assert.Equal(new DateTime(2026, 8, 18), record["sample_when"]);
            Assert.Equal("sample_parent", ((EntityReference)record["sample_parentid"]).LogicalName);
        }

        [Theory]
        [InlineData("Y", true)]
        [InlineData("no", false)]
        [InlineData("TRUE", true)]
        [InlineData(0d, false)]
        public void Accepts_the_yes_no_spellings_people_type(object cell, bool expected)
        {
            var sheet = Sheet(new object[] { "h" }, new[] { cell });
            var mapper = new SheetMapper(Table, new[] { new ColumnMapping(0, "sample_active", SheetValueType.Boolean) });

            var record = Assert.Single(mapper.Map(sheet).Records);

            Assert.Equal(expected, record["sample_active"]);
        }

        [Fact]
        public void A_bad_value_fails_only_its_own_row_and_reports_the_worksheet_row()
        {
            var sheet = Sheet(
                new object[] { "Name", "Count" },
                new object[] { "good", 1d },
                new object[] { "bad", "not a number" },
                new object[] { "also good", 3d });

            var mapper = new SheetMapper(Table, new[]
            {
                new ColumnMapping(0, "sample_name"),
                new ColumnMapping(1, "sample_count", SheetValueType.Integer)
            });

            var result = mapper.Map(sheet);

            Assert.Equal(2, result.Records.Count);
            var error = Assert.Single(result.Errors);
            Assert.Equal(3, error.RowNumber);
            Assert.Equal("sample_count", error.Column);
        }

        [Fact]
        public void A_fractional_value_is_rejected_for_a_whole_number_column()
        {
            var sheet = Sheet(new object[] { "h" }, new object[] { 1.5d });
            var mapper = new SheetMapper(Table, new[] { new ColumnMapping(0, "sample_count", SheetValueType.Integer) });

            var result = mapper.Map(sheet);

            Assert.Empty(result.Records);
            Assert.Contains("whole number", Assert.Single(result.Errors).Message);
        }

        [Fact]
        public void Missing_required_values_are_reported_and_blank_rows_are_ignored()
        {
            var sheet = Sheet(
                new object[] { "Name", "Count" },
                new object[] { null, 1d },
                new object[] { null, null },
                new object[] { "fine", 2d });

            var mapper = new SheetMapper(Table, new[]
            {
                new ColumnMapping(0, "sample_name") { Required = true },
                new ColumnMapping(1, "sample_count", SheetValueType.Integer)
            });

            var result = mapper.Map(sheet);

            Assert.Single(result.Records);
            // Row 3 is entirely blank: trailing selection, not an error.
            Assert.Equal(2, Assert.Single(result.Errors).RowNumber);
        }

        [Fact]
        public void Row_numbers_honour_the_block_offset()
        {
            var sheet = Sheet(
                new object[] { "Name" },
                new object[] { "a" });

            var mapper = new SheetMapper(Table, new[] { new ColumnMapping(0, "sample_name") });

            var result = mapper.Map(sheet, skipFirstRow: true, firstRowNumber: 10);

            Assert.Equal(11, result.SourceRowNumbers.Single());
        }
    }
}
