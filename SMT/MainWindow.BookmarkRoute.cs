using SMT.EVEData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SMT
{
    public partial class MainWindow
    {
        private BookmarkRoute bookmarkRouteResult;
        private string bookmarkRouteStartSystem;
        private bool bookmarkRoutePanelReady;
        private int bookmarkCalcGeneration;
        private bool bookmarkPushInFlight;

        private void InitBookmarkRoutePanel(List<EVEData.System> globalSystemList)
        {
            BookmarkAvoidSystemDropDownAC.ItemsSource = globalSystemList;
            bookmarkRoutePanelReady = true;
            OnSelectedCharChangedEventHandler += (s, e) => ResetBookmarkRoutePanel();
        }


        private void ResetBookmarkRoutePanel()
        {
            if (!bookmarkRoutePanelReady)
            {
                return;
            }

            bookmarkCalcGeneration++; // any in-flight calculation's result gets discarded when it lands
            bookmarkRouteResult = null;
            bookmarkRouteStartSystem = null;
            bookmarkLinesPanel.Children.Clear();
            bookmarkRouteStatusLbl.Content = "";
            bookmarkUnreachableText.Text = "";
            bookmarkUnparsedText.Text = "";

            // Clear the map overlay : RegionControl/UniverseControl draw whatever's in these
            // properties, independently of ActiveRoute/CapitalRoute, so they need clearing explicitly.
            RegionUC.BookmarkRoute = null;
            RegionUC.BookmarkRouteStartSystem = null;
            UniverseUC.BookmarkRoute = null;
            UniverseUC.BookmarkRouteStartSystem = null;
            RegionUC.ReDrawMap();
            UniverseUC.ReDrawMap(false, false, true);
        }

        private void BookmarkDropIsolatedChk_Click(object sender, RoutedEventArgs e)
        {
            if (bookmarkRoutePanelReady && !string.IsNullOrWhiteSpace(bookmarkInputTextBox?.Text))
            {
                RunBookmarkRouteCalculation();
            }
        }

        private void CalculateBookmarkRouteBtn_Click(object sender, RoutedEventArgs e)
        {
            RunBookmarkRouteCalculation();
        }

        private void BookmarkRuleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (bookmarkKValueLbl != null)
            {
                bookmarkKValueLbl.Content = ((int)bookmarkKSlider.Value).ToString(CultureInfo.InvariantCulture);
            }

            if (bookmarkJValueLbl != null)
            {
                bookmarkJValueLbl.Content = ((int)bookmarkJSlider.Value).ToString(CultureInfo.InvariantCulture);
            }

            if (bookmarkIsoValueLbl != null)
            {
                bookmarkIsoValueLbl.Content = ((int)bookmarkIsoSlider.Value).ToString(CultureInfo.InvariantCulture);
            }

            if (bookmarkKeepBmValueLbl != null)
            {
                bookmarkKeepBmValueLbl.Content = ((int)bookmarkKeepBmSlider.Value).ToString(CultureInfo.InvariantCulture);
            }

            // Immediate recalculation on a K or J change, once a paste already exists.
            if (bookmarkRoutePanelReady && !string.IsNullOrWhiteSpace(bookmarkInputTextBox?.Text))
            {
                RunBookmarkRouteCalculation();
            }
        }

        private void AddBookmarkAvoidSystemsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (BookmarkAvoidSystemDropDownAC.SelectedItem == null)
            {
                return;
            }

            EVEData.System s = BookmarkAvoidSystemDropDownAC.SelectedItem as EVEData.System;
            if (s != null && !bookmarkAvoidLB.Items.Contains(s.Name))
            {
                bookmarkAvoidLB.Items.Add(s.Name);
            }
        }

        private void ClearBookmarkAvoidSystemsBtn_Click(object sender, RoutedEventArgs e)
        {
            bookmarkAvoidLB.Items.Clear();
        }

        private void RunBookmarkRouteCalculation()
        {
            if (!bookmarkRoutePanelReady)
            {
                return;
            }

            EVEData.LocalCharacter character = RegionUC.ActiveCharacter;
            if (character == null || string.IsNullOrEmpty(character.Location))
            {
                bookmarkRouteStatusLbl.Content = "No active character.";
                return;
            }

            string text = bookmarkInputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string startSystem = character.Location;
            int k = (int)bookmarkKSlider.Value;
            int jumpCost = (int)bookmarkJSlider.Value;

            // 0 disables the filter in the solver, so the checkbox is just "pass 0".
            int isolationJumps = bookmarkDropIsolatedChk.IsChecked == true ? (int)bookmarkIsoSlider.Value : 0;
            int isolationKeepBookmarks = (int)bookmarkKeepBmSlider.Value;
            if (!decimal.TryParse(bookmarkMaxLYTextBox.Text, out decimal maxLY))
            {
                maxLY = 6.0m;
            }
            bool avoidHighSec = bookmarkAvoidHighSecChk.IsChecked == true;
            List<string> avoidList = bookmarkAvoidLB.Items.Cast<string>().ToList();

            // All computation runs off the UI thread : this can walk the whole galaxy graph
            // N+1 times, and the existing CapitalRoute.Recalculate() calls in this file that run straight
            // in a Click handler are exactly the freeze this must not copy.
            int generation = ++bookmarkCalcGeneration;
            CalculateBookmarkRouteBtn.IsEnabled = false;
            bookmarkRouteStatusLbl.Content = "Calculating...";

            Task.Run(() =>
            {
                BookmarkRoute result = null;
                string error = null;

                // Without this the task's exception is never observed : the Dispatcher callback below would
                // never run, leaving the button disabled and the label stuck on "Calculating..." forever.
                try
                {
                    result = BookmarkRouteAdapter.PlanRoute(text, startSystem, k, jumpCost, maxLY, isolationJumps, isolationKeepBookmarks, avoidHighSec, avoidList);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (generation != bookmarkCalcGeneration)
                    {
                        return; // superseded by a newer calculation (e.g. rapid K slider drags)
                    }

                    CalculateBookmarkRouteBtn.IsEnabled = true;

                    if (error != null)
                    {
                        bookmarkRouteStatusLbl.Content = "Calculation failed: " + error;
                        return;
                    }

                    bookmarkRouteStartSystem = startSystem;
                    RenderBookmarkResult(result);
                });
            });
        }

        private void RenderBookmarkResult(BookmarkRoute result)
        {
            bookmarkRouteResult = result;
            bookmarkLinesPanel.Children.Clear();

            int totalSystems = result.BookmarkCounts.Count;
            bookmarkRouteStatusLbl.Content = totalSystems == 0
                ? "No systems parsed from the pasted text."
                : $"{result.Lines.Count} line(s) · {result.PlannedTargets}/{result.TotalTargets} systems · {result.PlannedBookmarks}/{result.TotalBookmarks} bookmarks · {result.BookmarkDiscardRate:P0} discarded";

            for (int i = 0; i < result.Lines.Count; i++)
            {
                RouteLine line = result.Lines[i];

                if (line.EntryJumpLY > 0)
                {
                    bookmarkLinesPanel.Children.Add(new TextBlock
                    {
                        Text = $"⇒ Capital jump, {line.EntryJumpLY:0.##} LY ⇒",
                        Margin = new Thickness(2),
                        FontStyle = FontStyles.Italic,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }

                GroupBox lineBox = new GroupBox { Header = $"Line {i + 1}  ({line.TargetCount} points / {line.GateJumps} gate jumps)", Margin = new Thickness(2) };
                StackPanel lineContent = new StackPanel();

                ListBox targetsLb = new ListBox { MaxHeight = 140 };
                foreach (string sysName in line.Targets)
                {
                    int count = result.BookmarkCounts.GetValueOrDefault(sysName);
                    targetsLb.Items.Add($"{sysName}  ({count} bookmark{(count == 1 ? "" : "s")})");
                }
                lineContent.Children.Add(targetsLb);

                Button applyBtn = new Button { Content = "Apply", Margin = new Thickness(2), Tag = line };
                applyBtn.Click += ApplyBookmarkLine_Click;
                lineContent.Children.Add(applyBtn);

                lineBox.Content = lineContent;
                bookmarkLinesPanel.Children.Add(lineBox);
            }

            bookmarkUnreachableGroupBox.Header = $"Unreachable ({result.UnreachableSystems.Count})";
            bookmarkUnreachableText.Text = string.Join(", ", result.UnreachableSystems);

            bookmarkIsolatedGroupBox.Header = $"Isolated, dropped ({result.IsolatedSystems.Count})";
            bookmarkIsolatedText.Text = string.Join(", ", result.IsolatedSystems);

            bookmarkUnparsedGroupBox.Header = $"Unparsed ({result.UnparsedLines.Count})";
            bookmarkUnparsedText.Text = string.Join("\n", result.UnparsedLines);

            // Hand the result to the map overlays. Independent of ActiveRoute/CapitalRoute --
            // these are separate properties the two controls draw alongside their existing route rendering.
            RegionUC.BookmarkRoute = result;
            RegionUC.BookmarkRouteStartSystem = bookmarkRouteStartSystem;
            UniverseUC.BookmarkRoute = result;
            UniverseUC.BookmarkRouteStartSystem = bookmarkRouteStartSystem;
            RegionUC.ReDrawMap();
            UniverseUC.ReDrawMap(false, false, true);
        }

        private async void ApplyBookmarkLine_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            RouteLine line = btn?.Tag as RouteLine;
            if (line == null)
            {
                return;
            }

            // One push at a time. A line takes seconds to push (one ESI call per waypoint, 200ms apart), and
            // two overlapping pushes would each send clearOtherWaypoints=true on their first waypoint and then
            // interleave, leaving a spliced in-game route : the exact thing per-line applying exists to avoid
            //. Only the clicked button gets disabled, so this guard covers the other lines.
            if (bookmarkPushInFlight)
            {
                bookmarkRouteStatusLbl.Content = "Another line is still being applied.";
                return;
            }

            EVEData.LocalCharacter character = RegionUC.ActiveCharacter;
            if (character == null)
            {
                bookmarkRouteStatusLbl.Content = "No active character.";
                return;
            }

            bookmarkPushInFlight = true;
            btn.IsEnabled = false;
            bookmarkRouteStatusLbl.Content = "Applying line...";

            try
            {
                // ApplyLineAsync only awaits ESI I/O and Task.Delay -- no CPU-bound work -- so awaiting it
                // directly on the UI thread doesn't freeze the app.
                string error = await BookmarkRoutePusher.ApplyLineAsync(character, line);
                bookmarkRouteStatusLbl.Content = error ?? "Line applied.";
            }
            catch (Exception ex)
            {
                // async void event handler : an escaping exception would take the process down.
                bookmarkRouteStatusLbl.Content = "Apply failed: " + ex.Message;
            }
            finally
            {
                bookmarkPushInFlight = false;
                btn.IsEnabled = true;
            }
        }

    }
}
