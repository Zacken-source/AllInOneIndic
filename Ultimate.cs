using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo
{
    // My first test putting the indicators I use on one chart:
    // higher timeframe boxes, sessions, ranges, fibonacci and a basic elliott count.
    // Each layer can be turned on/off with the buttons on the chart.
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class AllInOne : Indicator
    {
        [Parameter("Bars to scan", DefaultValue = 1500, MinValue = 200, Group = "General")]
        public int BarsToScan { get; set; }

        [Parameter("Show buttons", DefaultValue = true, Group = "General")]
        public bool ShowButtons { get; set; }

        [Parameter("Daily candles", DefaultValue = 3, MinValue = 1, Group = "Boxes")]
        public int DailyCandles { get; set; }

        [Parameter("H4 candles", DefaultValue = 6, MinValue = 1, Group = "Boxes")]
        public int H4Candles { get; set; }

        [Parameter("H1 candles", DefaultValue = 6, MinValue = 1, Group = "Boxes")]
        public int H1Candles { get; set; }

        [Parameter("Days to show", DefaultValue = 5, MinValue = 1, Group = "Sessions")]
        public int SessionDays { get; set; }

        [Parameter("Summer time (London/NY -1h)", DefaultValue = false, Group = "Sessions")]
        public bool SummerTime { get; set; }

        [Parameter("Max height (ATR)", DefaultValue = 2.5, MinValue = 0.5, Group = "Ranges")]
        public double RangeAtr { get; set; }

        [Parameter("Min bars", DefaultValue = 20, MinValue = 5, Group = "Ranges")]
        public int RangeMinBars { get; set; }

        [Parameter("Zigzag size (ATR)", DefaultValue = 2.0, MinValue = 0.5, Group = "Fibo / Elliott")]
        public double ZigzagAtr { get; set; }

        private class Point
        {
            public int Index;
            public double Price;
            public bool IsHigh;
        }

        private AverageTrueRange atr;
        private Bars daily, h4, h1;
        private Dictionary<string, bool> layers = new Dictionary<string, bool>();
        private List<string> objects = new List<string>();
        private int lastIndex = -1;
        private int counter = 0;
        private int first, last;

        protected override void Initialize()
        {
            atr = Indicators.AverageTrueRange(14, MovingAverageType.Simple);
            daily = MarketData.GetBars(TimeFrame.Daily);
            h4 = MarketData.GetBars(TimeFrame.Hour4);
            h1 = MarketData.GetBars(TimeFrame.Hour);

            layers["Boxes"] = true;
            layers["Sessions"] = true;
            layers["Ranges"] = true;
            layers["Fibo"] = true;
            layers["Elliott"] = true;

            if (ShowButtons)
                AddButtons();
        }

        public override void Calculate(int index)
        {
            if (!IsLastBar || index == lastIndex)
                return;
            lastIndex = index;
            Redraw();
        }

        private void Redraw()
        {
            foreach (var name in objects)
                Chart.RemoveObject(name);
            objects.Clear();

            last = Bars.Count - 2;  // last closed candle
            first = Math.Max(20, last - BarsToScan);
            if (last - first < 50)
                return;

            if (layers["Boxes"]) DrawBoxes();
            if (layers["Sessions"]) DrawSessions();
            if (layers["Ranges"]) DrawRanges();

            var points = Zigzag();
            if (layers["Fibo"]) DrawFibo(points);
            if (layers["Elliott"]) DrawElliott(points);
        }

        private void AddButtons()
        {
            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = 5
            };

            foreach (var key in new List<string>(layers.Keys))
            {
                string layer = key;
                var button = new Button
                {
                    Text = layer,
                    Width = 80,
                    Margin = 2,
                    BackgroundColor = Color.SeaGreen
                };
                button.Click += args =>
                {
                    layers[layer] = !layers[layer];
                    button.BackgroundColor = layers[layer] ? Color.SeaGreen : Color.DimGray;
                    Redraw();
                };
                panel.AddChild(button);
            }
            Chart.AddControl(panel);
        }

        // ---------- higher timeframe boxes ----------

        private void DrawBoxes()
        {
            DrawBox(daily, DailyCandles, "D1", Color.Orange);
            DrawBox(h4, H4Candles, "H4", Color.Yellow);
            DrawBox(h1, H1Candles, "H1", Color.LimeGreen);
        }

        // high/low of the last N candles of a higher timeframe
        private void DrawBox(Bars bars, int candles, string label, Color color)
        {
            if (bars.Count < candles)
                return;

            double high = double.MinValue, low = double.MaxValue;
            for (int i = bars.Count - candles; i < bars.Count; i++)
            {
                high = Math.Max(high, bars.HighPrices[i]);
                low = Math.Min(low, bars.LowPrices[i]);
            }

            DateTime start = bars.OpenTimes[bars.Count - candles];
            var rect = Chart.DrawRectangle(NewName(), start, high, Bars.OpenTimes.LastValue, low, Color.FromArgb(40, color));
            rect.IsFilled = true;

            double pips = (high - low) / Symbol.PipSize;
            var text = Chart.DrawText(NewName(), label + " x" + candles + " (" + pips.ToString("F0") + " pips)", start, high, color);
            text.FontSize = 8;
            text.VerticalAlignment = VerticalAlignment.Top;
        }

        // ---------- sessions ----------

        private void DrawSessions()
        {
            if (TimeFrame == TimeFrame.Daily || TimeFrame == TimeFrame.Weekly || TimeFrame == TimeFrame.Monthly)
                return;

            int shift = SummerTime ? 1 : 0;
            DateTime day = Bars.OpenTimes.LastValue.Date;
            int count = 0;

            while (count < SessionDays && day >= Bars.OpenTimes[first].Date)
            {
                if (day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday)
                {
                    DrawSession("Asia", day.AddHours(0), day.AddHours(8), Color.Magenta);
                    DrawSession("London", day.AddHours(8 - shift), day.AddHours(17 - shift), Color.Gold);
                    DrawSession("New York", day.AddHours(13 - shift), day.AddHours(22 - shift), Color.DodgerBlue);
                    count++;
                }
                day = day.AddDays(-1);
            }
        }

        private void DrawSession(string name, DateTime start, DateTime end, Color color)
        {
            double high = double.MinValue, low = double.MaxValue;
            bool found = false;
            for (int i = first; i <= last + 1; i++)
            {
                if (Bars.OpenTimes[i] < start || Bars.OpenTimes[i] >= end)
                    continue;
                high = Math.Max(high, Bars.HighPrices[i]);
                low = Math.Min(low, Bars.LowPrices[i]);
                found = true;
            }
            if (!found)
                return;

            var rect = Chart.DrawRectangle(NewName(), start, high, end, low, Color.FromArgb(35, color));
            rect.IsFilled = true;
            var text = Chart.DrawText(NewName(), name, start, high, color);
            text.FontSize = 8;
            text.VerticalAlignment = VerticalAlignment.Top;
        }

        // ---------- ranges ----------

        private void DrawRanges()
        {
            int i = first;
            while (i < last)
            {
                double top = Bars.HighPrices[i], bottom = Bars.LowPrices[i];
                int j = i;
                while (j + 1 <= last)
                {
                    double newTop = Math.Max(top, Bars.HighPrices[j + 1]);
                    double newBottom = Math.Min(bottom, Bars.LowPrices[j + 1]);
                    if (newTop - newBottom > RangeAtr * atr.Result[j + 1])
                        break;
                    top = newTop;
                    bottom = newBottom;
                    j++;
                }

                if (j - i + 1 >= RangeMinBars)
                {
                    var rect = Chart.DrawRectangle(NewName(), i, top, j + 1, bottom, Color.FromArgb(40, Color.SkyBlue));
                    rect.IsFilled = true;
                    i = j + 1;
                }
                else
                {
                    i++;
                }
            }
        }

        // ---------- zigzag (used by fibo and elliott) ----------

        private List<Point> Zigzag()
        {
            var points = new List<Point>();
            int direction = 0;
            int highIndex = first, lowIndex = first;
            double high = Bars.HighPrices[first], low = Bars.LowPrices[first];

            for (int i = first + 1; i <= last; i++)
            {
                double size = ZigzagAtr * atr.Result[i];

                if (direction >= 0 && Bars.HighPrices[i] > high)
                {
                    high = Bars.HighPrices[i];
                    highIndex = i;
                }
                if (direction <= 0 && Bars.LowPrices[i] < low)
                {
                    low = Bars.LowPrices[i];
                    lowIndex = i;
                }

                if (direction >= 0 && high - Bars.LowPrices[i] >= size && i > highIndex)
                {
                    points.Add(new Point { Index = highIndex, Price = high, IsHigh = true });
                    direction = -1;
                    low = Bars.LowPrices[i];
                    lowIndex = i;
                }
                else if (direction <= 0 && Bars.HighPrices[i] - low >= size && i > lowIndex)
                {
                    points.Add(new Point { Index = lowIndex, Price = low, IsHigh = false });
                    direction = 1;
                    high = Bars.HighPrices[i];
                    highIndex = i;
                }
            }

            // current leg, not confirmed yet
            if (direction == 1)
                points.Add(new Point { Index = highIndex, Price = high, IsHigh = true });
            else if (direction == -1)
                points.Add(new Point { Index = lowIndex, Price = low, IsHigh = false });

            return points;
        }

        // ---------- fibonacci on the last swing ----------

        private void DrawFibo(List<Point> points)
        {
            if (points.Count < 2)
                return;

            var a = points[points.Count - 2];
            var b = points[points.Count - 1];
            double move = b.Price - a.Price;
            double[] levels = { 0, 0.382, 0.5, 0.618, 0.786, 1 };

            foreach (double r in levels)
            {
                double price = b.Price - move * r;
                Chart.DrawTrendLine(NewName(), a.Index, price, last + 10, price, Color.White, 1, LineStyle.Dots);
                var text = Chart.DrawText(NewName(), (r * 100).ToString("0.0") + "%", last + 10, price, Color.White);
                text.FontSize = 8;
                text.VerticalAlignment = VerticalAlignment.Center;
            }

            var zone = Chart.DrawRectangle(NewName(), a.Index, b.Price - move * 0.618, last + 10, b.Price - move * 0.786, Color.FromArgb(50, Color.Gold));
            zone.IsFilled = true;
        }

        // ---------- elliott (basic rules only) ----------

        private void DrawElliott(List<Point> points)
        {
            for (int end = points.Count - 1; end >= 5; end--)
            {
                int start = end - 5;
                if (!IsImpulse(points, start))
                    continue;

                for (int k = 0; k <= 5; k++)
                {
                    var p = points[start + k];
                    if (k > 0)
                    {
                        var prev = points[start + k - 1];
                        Chart.DrawTrendLine(NewName(), prev.Index, prev.Price, p.Index, p.Price, Color.Orange, 2, LineStyle.Solid);
                    }
                    var text = Chart.DrawText(NewName(), k.ToString(), p.Index, p.Price, Color.Orange);
                    text.HorizontalAlignment = HorizontalAlignment.Center;
                    text.VerticalAlignment = p.IsHigh ? VerticalAlignment.Top : VerticalAlignment.Bottom;
                }
                return;
            }
        }

        private bool IsImpulse(List<Point> p, int start)
        {
            double sign = p[start].IsHigh ? -1 : 1;
            double w0 = sign * p[start].Price;
            double w1 = sign * p[start + 1].Price;
            double w2 = sign * p[start + 2].Price;
            double w3 = sign * p[start + 3].Price;
            double w4 = sign * p[start + 4].Price;
            double w5 = sign * p[start + 5].Price;

            if (w2 <= w0) return false;              // 2 doesn't go below the start of 1
            if (w3 <= w1 || w5 <= w3) return false;  // 3 above 1, 5 above 3
            if (w4 <= w1) return false;              // 4 doesn't overlap 1

            double len1 = w1 - w0, len3 = w3 - w2, len5 = w5 - w4;
            return !(len3 < len1 && len3 < len5);    // 3 is never the shortest
        }

        private string NewName()
        {
            string name = "allinone_" + counter++;
            objects.Add(name);
            return name;
        }
    }
}
