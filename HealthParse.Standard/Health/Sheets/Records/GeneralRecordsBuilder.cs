﻿using System;
using System.Collections.Generic;
using System.Linq;
using NodaTime;

namespace HealthParse.Standard.Health.Sheets.Records
{
    public class GeneralRecordsBuilder : IRawSheetBuilder<LocalDate>, ISummarySheetBuilder<LocalDate>, IMonthlySummaryBuilder<LocalDate>
    {
        private readonly DateTimeZone _zone;
        private readonly Settings.Settings _settings;
        private readonly List<Tuple<LocalDate, int>> _standing;
        private readonly List<Tuple<LocalDate, int>> _flightsClimbed;
        private readonly List<Tuple<LocalDate, double>> _exerciseTime;

        public GeneralRecordsBuilder(IEnumerable<Record> records, DateTimeZone zone, Settings.Settings settings)
        {
            _zone = zone;
            _settings = settings;

            var categorized = records.Aggregate(new { standing = new List<Record>(), exercise = new List<Record>(), flights = new List<Record>() }, (accum, record) =>
            {
                if (record.Type == HKConstants.Records.Standing.StandType)
                    accum.standing.Add(record);
                if (record.Type == HKConstants.Records.ExerciseTime)
                    accum.exercise.Add(record);
                if (record.Type == HKConstants.Records.FlightsClimbed)
                    accum.flights.Add(record);

                return accum;
            });

            _standing =  categorized.standing
                .Where(r => r.Value == HKConstants.Records.Standing.Stood)
                .GroupBy(r => r.StartDate.InZone(_zone).Date)
                .Select(r => Tuple.Create(r.Key, r.Count()))
                .ToList();

            _exerciseTime = categorized.exercise
                .GroupBy(r => r.StartDate.InZone(_zone).Date)
                .Select(r => Tuple.Create(r.Key, new UnitsNet.Duration(r.Sum(c => (c.EndDate - c.StartDate).TotalSeconds)).As(settings.DurationUnit)))
                .ToList();

            _flightsClimbed = categorized.flights
                .GroupBy(r => r.StartDate.InZone(_zone).Date)
                .Select(r => Tuple.Create(r.Key, r.Sum(c => (int)c.Value.SafeParse(0))))
                .ToList();
        }

        public Dataset<LocalDate> BuildRawSheet()
        {
            var dates = _standing.Select(s => s.Item1)
                .Concat(_exerciseTime.Select(s => s.Item1))
                .Concat(_flightsClimbed.Select(s => s.Item1))
                .Distinct();

            return new Dataset<LocalDate>(
                new KeyColumn<LocalDate>(dates),
                MakeColumn(_standing, ColumnNames.StandHours()),
                MakeColumn(_flightsClimbed, ColumnNames.FlightsClimbed()),
                MakeColumn(_exerciseTime, ColumnNames.ExerciseDuration(_settings.DurationUnit)));
        }

        public IEnumerable<Column<LocalDate>> BuildSummary()
        {
            var standingData = _standing
                .GroupBy(s => new { s.Item1.Year, s.Item1.Month })
                .Select(r => Tuple.Create(new LocalDate(r.Key.Year, r.Key.Month, 1), r.Average(c => c.Item2)));

            var flightsData = _flightsClimbed
                .GroupBy(s => new { s.Item1.Year, s.Item1.Month })
                .Select(r => Tuple.Create(new LocalDate(r.Key.Year, r.Key.Month, 1), r.Sum(c => c.Item2)));

            var exerciseTimeData = _exerciseTime
                .GroupBy(s => new { s.Item1.Year, s.Item1.Month })
                .Select(r => Tuple.Create(new LocalDate(r.Key.Year, r.Key.Month, 1), r.Sum(c => c.Item2)));

            yield return MakeColumn(standingData, ColumnNames.AverageStandHours());
            yield return MakeColumn(flightsData, ColumnNames.TotalFlightsClimbed());
            yield return MakeColumn(exerciseTimeData, ColumnNames.TotalExerciseDuration(_settings.DurationUnit));
        }

        public IEnumerable<Column<LocalDate>> BuildSummaryForDateRange(IRange<ZonedDateTime> dateRange)
        {
            yield return MakeColumn(
                _standing.Where(r => dateRange.Includes(r.Item1.AtStartOfDayInZone(_zone), Clusivity.Inclusive)),
                ColumnNames.StandHours());

            yield return MakeColumn(
                _flightsClimbed.Where(r => dateRange.Includes(r.Item1.AtStartOfDayInZone(_zone), Clusivity.Inclusive)),
                ColumnNames.FlightsClimbed());

            yield return MakeColumn(
                _exerciseTime.Where(r => dateRange.Includes(r.Item1.AtStartOfDayInZone(_zone), Clusivity.Inclusive)),
                ColumnNames.ExerciseDuration(_settings.DurationUnit));
        }
        private static Column<TKey> MakeColumn<TKey, TVal>(IEnumerable<Tuple<TKey, TVal>> data, string header = null, string range = null)
        {
            return data.Aggregate(
                new Column<TKey> { Header = header, RangeName = range },
                (col, r) => { col.Add(r.Item1, r.Item2); return col; });
        }
    }
}