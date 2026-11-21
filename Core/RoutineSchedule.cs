using System.Text.Json.Serialization;

namespace Levitation.Core;

public enum RepeatFrequency { Once, Daily, Weekly, Workdays, Monthly, Yearly }
public enum MonthlyRule { DayOfMonth, FirstDay, LastDay, NthWeekday, LastWeekday }
public enum MissingMonthDay { Skip, UseLastDay }

public sealed record MonthlySchedule(
    MonthlyRule Rule = MonthlyRule.DayOfMonth, int Day = 1, int Occurrence = 1,
    DayOfWeek Weekday = DayOfWeek.Monday, MissingMonthDay MissingDay = MissingMonthDay.Skip)
{
    internal bool IsValid => Enum.IsDefined(Rule) && Enum.IsDefined(MissingDay) &&
        Day is >= 1 and <= 31 && Occurrence is >= 1 and <= 5 && Enum.IsDefined(Weekday);

    internal int DayInMonth(int year, int month)
    {
        int length = DateTime.DaysInMonth(year, month);
        return Rule switch
        {
            MonthlyRule.FirstDay => 1,
            MonthlyRule.LastDay => length,
            MonthlyRule.NthWeekday => 1 + WeekdayOffset(Weekday, new DateOnly(year, month, 1).DayOfWeek) + 7 * (Occurrence - 1),
            MonthlyRule.LastWeekday => length - WeekdayOffset(new DateOnly(year, month, length).DayOfWeek, Weekday),
            _ => MissingDay == MissingMonthDay.UseLastDay ? Math.Min(Day, length) : Day
        };
    }

    internal string Description => Rule switch
    {
        MonthlyRule.FirstDay => "第一天",
        MonthlyRule.LastDay => "最后一天",
        MonthlyRule.NthWeekday => $"第 {Occurrence} 个星期{ChineseDay(Weekday)}",
        MonthlyRule.LastWeekday => $"最后一个星期{ChineseDay(Weekday)}",
        _ => $"{Day} 日" + (MissingDay == MissingMonthDay.UseLastDay ? "（无该日则月末）" : "")
    };

    internal static int WeekdayOffset(DayOfWeek day, DayOfWeek origin) => ((int)day - (int)origin + 7) % 7;
    internal static char ChineseDay(DayOfWeek day) => "日一二三四五六"[(int)day];
}

public sealed record RoutineSubtask(string Title, List<RoutineSubtask> Children)
{
    internal static bool IsValid(RoutineSubtask? node, int depth) =>
        depth <= TaskTree.MaximumDepth && node != null && !string.IsNullOrWhiteSpace(node.Title) &&
        node.Children != null && node.Children.All(child => IsValid(child, depth + 1));
}

public sealed class Routine : PersistentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public RepeatFrequency Frequency { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public List<DayOfWeek> Weekdays { get; set; } = [];
    public int Interval { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MonthlySchedule? Monthly { get; set; }
    public List<RoutineSubtask> Subtasks { get; set; } = [];

    private MonthlySchedule EffectiveMonth => Monthly ?? new(Day: StartDate.Day);
    private static int MonthIndex(DateOnly date) => (date.Year - 1) * 12 + date.Month - 1;
    private static int WeekStart(DateOnly date) => date.DayNumber - MonthlySchedule.WeekdayOffset(date.DayOfWeek, DayOfWeek.Monday);

    public bool IsDue(DateOnly day)
    {
        if (day < StartDate || Interval is < 1 or > 99) return false;
        return Frequency switch
        {
            RepeatFrequency.Once => true, // Preserve one-time catch-up after a missed launch.
            RepeatFrequency.Daily => (day.DayNumber - StartDate.DayNumber) % Interval == 0,
            RepeatFrequency.Workdays => day.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday,
            RepeatFrequency.Weekly => Weekdays.Contains(day.DayOfWeek) && (WeekStart(day) - WeekStart(StartDate)) / 7 % Interval == 0,
            RepeatFrequency.Monthly => (MonthIndex(day) - MonthIndex(StartDate)) % Interval == 0 &&
                day.Day == EffectiveMonth.DayInMonth(day.Year, day.Month),
            RepeatFrequency.Yearly => day.Month == StartDate.Month && (day.Year - StartDate.Year) % Interval == 0 &&
                day.Day == Math.Min(StartDate.Day, DateTime.DaysInMonth(day.Year, day.Month)),
            _ => false
        };
    }

    public IEnumerable<DateOnly> Upcoming(DateOnly from, int count = 3)
    {
        if (count <= 0 || !Enum.IsDefined(Frequency) || Interval is < 1 or > 99) yield break;
        if (Frequency == RepeatFrequency.Once) { yield return StartDate; yield break; }
        if (Frequency == RepeatFrequency.Weekly && Weekdays.Count == 0) yield break;
        var first = from > StartDate ? from : StartDate;
        if (Frequency is RepeatFrequency.Monthly or RepeatFrequency.Yearly)
        {
            int step = Interval * (Frequency == RepeatFrequency.Yearly ? 12 : 1);
            int origin = MonthIndex(StartDate);
            var monthly = EffectiveMonth;
            for (int index = origin + (MonthIndex(first) - origin) / step * step; index < 9999 * 12; index += step)
            {
                int year = index / 12 + 1, month = index % 12 + 1;
                int length = DateTime.DaysInMonth(year, month);
                int day = Frequency == RepeatFrequency.Monthly ? monthly.DayInMonth(year, month) : Math.Min(StartDate.Day, length);
                if (day < 1 || day > length) continue;
                var candidate = new DateOnly(year, month, day);
                if (candidate < first) continue;
                yield return candidate;
                if (--count == 0) yield break;
            }
            yield break;
        }

        for (int number = first.DayNumber; number <= DateOnly.MaxValue.DayNumber; number++)
        {
            var day = DateOnly.FromDayNumber(number);
            if (!IsDue(day)) continue;
            yield return day;
            if (--count == 0) yield break;
        }
    }

    internal bool IsValid => Title != null && Enum.IsDefined(Frequency) && Interval is >= 1 and <= 99 &&
        (Interval == 1 || Frequency is not (RepeatFrequency.Once or RepeatFrequency.Workdays)) &&
        Weekdays != null && Weekdays.All(Enum.IsDefined) && (Monthly == null || Monthly.IsValid) &&
        Subtasks != null && Subtasks.All(child => RoutineSubtask.IsValid(child, 1));

    internal bool RequiresNewSchema => Frequency is RepeatFrequency.Workdays or RepeatFrequency.Monthly or RepeatFrequency.Yearly ||
        Interval != 1 || Subtasks.Count > 0;

    [JsonIgnore]
    public string Description
    {
        get
        {
            string Every(string unit, string single) => Interval == 1 ? single : $"每隔 {Interval} {unit}";
            string pattern = Frequency switch
            {
                RepeatFrequency.Daily => Every("天", "每天"),
                RepeatFrequency.Workdays => "周一至周五",
                RepeatFrequency.Weekly => Every("周", "每周") + " · " + string.Join("、", Weekdays.OrderBy(d =>
                    MonthlySchedule.WeekdayOffset(d, DayOfWeek.Monday)).Select(d => $"周{MonthlySchedule.ChineseDay(d)}")),
                RepeatFrequency.Monthly => Every("个月", "每月") + " · " + EffectiveMonth.Description,
                RepeatFrequency.Yearly => Every("年", "每年") + $" · {StartDate:M月d日}",
                _ => "仅一次"
            };
            return Frequency == RepeatFrequency.Once ? $"{StartDate:yyyy-MM-dd} · {pattern}" : $"{pattern} · {StartDate:yyyy-MM-dd} 起";
        }
    }
}
