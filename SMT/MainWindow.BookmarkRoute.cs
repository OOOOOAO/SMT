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

        // One slot per character : switching characters only ever restores what's cached here or
        // shows an empty panel, it never recalculates. Recalculating a route is exclusively the
        // "Calculate route" button's job (and the K/J/isolation controls, which already recalc on
        // change once a paste exists -- switching characters must not join that list).
        private readonly Dictionary<long, (string StartSystem, BookmarkRoute Result)> bookmarkRouteCache = new Dictionary<long, (string StartSystem, BookmarkRoute Result)>();
        private long? bookmarkRouteCacheOwnerId;

        private void InitBookmarkRoutePanel(List<EVEData.System> globalSystemList)
        {
            BookmarkAvoidSystemDropDownAC.ItemsSource = globalSystemList;
            BookmarkStartSystemDropDownAC.ItemsSource = globalSystemList;
            bookmarkRoutePanelReady = true;
            OnSelectedCharChangedEventHandler += (s, e) => SwitchBookmarkRouteCharacter();

            // The map's own selection changes under us (click, dropdown, follow-character), so the
            // start line has to be refreshed from outside : nothing in this panel triggers it.
            RegionUC.SelectedSystemChanged += OnMapSelectedSystemChanged;

            UpdateBookmarkStartInfo();
        }

        /// <summary>
        /// The system a calculation starts from, in precedence order : the map's selected system when
        /// the panel is set to follow it, else the system picked in the panel, else wherever the active
        /// character is. Null when none of those resolve, which the caller reports instead of guessing.
        /// </summary>
        private string ResolveBookmarkStartSystem(out string source)
        {
            if (bookmarkStartUseSelectedChk.IsChecked == true && !string.IsNullOrEmpty(RegionUC.SelectedSystem))
            {
                source = GetResourceText("Main_BM_StartFromMap", "map selection");
                return RegionUC.SelectedSystem;
            }

            EVEData.System picked = BookmarkStartSystemDropDownAC.SelectedItem as EVEData.System;
            if (picked == null)
            {
                // Nothing chosen off the list, but the box is editable : a typed name is a legitimate
                // start, and silently planning from somewhere else would look like it worked.
                string typed = BookmarkStartSystemDropDownAC.Text;
                if (!string.IsNullOrWhiteSpace(typed))
                {
                    picked = EVEManager.GetEveSystem(typed.Trim());
                }
            }

            if (picked != null)
            {
                source = GetResourceText("Main_BM_StartFromPanel", "panel selection");
                return picked.Name;
            }

            source = GetResourceText("Main_BM_StartFromChar", "character location");
            return RegionUC.ActiveCharacter?.Location;
        }

        private void UpdateBookmarkStartInfo()
        {
            if (!bookmarkRoutePanelReady)
            {
                return;
            }

            string start = ResolveBookmarkStartSystem(out string source);

            if (string.IsNullOrEmpty(start))
            {
                bookmarkStartInfoLbl.Content = GetResourceText("Main_BM_LblStartNone", "Start : not set");
                return;
            }

            // Via a local, like the capital-route summary above : passing the resource straight into
            // string.Format trips CA1863.
            string format = GetResourceText("Main_BM_LblStartFmt", "Start : {0}  ({1})");
            bookmarkStartInfoLbl.Content = string.Format(CultureInfo.CurrentCulture, format, start, source);
        }

        private void OnMapSelectedSystemChanged(string system)
        {
            UpdateBookmarkStartInfo();

            // Nothing recalculates on a map click : browsing the map would re-plan on every click, and a
            // followed character would re-plan on every gate jump. Say the route is stale instead of
            // leaving one on screen that quietly starts somewhere else.
            if (bookmarkRouteResult != null)
            {
                string start = ResolveBookmarkStartSystem(out _);
                if (!string.IsNullOrEmpty(start) && !string.Equals(start, bookmarkRouteStartSystem, StringComparison.OrdinalIgnoreCase))
                {
                    bookmarkRouteStatusLbl.Content = GetResourceText("Main_BM_MsgStartChanged", "Start changed : recalculate to update the route.");
                }
            }
        }

        private void BookmarkStartSystemDropDownAC_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!bookmarkRoutePanelReady)
            {
                return;
            }

            // Label only, deliberately : an editable auto-complete box can raise this while the user is
            // still typing, and every raise would be a full replan. The start is applied by the Calculate
            // button (or by the "use selected system" button, which is an explicit click), and the status
            // line says so when the plan on screen no longer matches the start shown here.
            UpdateBookmarkStartInfo();
        }

        private void UseSelectedBookmarkStartBtn_Click(object sender, RoutedEventArgs e)
        {
            string selected = RegionUC.SelectedSystem;
            if (string.IsNullOrEmpty(selected))
            {
                bookmarkRouteStatusLbl.Content = GetResourceText("Main_BM_MsgNoMapSelection", "No system is selected on the map.");
                return;
            }

            // Assign the item and the text : the box is editable, so a stale typed name could otherwise
            // outlive the pick, and re-assigning the item that's already selected raises nothing.
            BookmarkStartSystemDropDownAC.SelectedItem = EVEManager.GetEveSystem(selected);
            BookmarkStartSystemDropDownAC.Text = selected;

            UpdateBookmarkStartInfo();

            if (!string.IsNullOrWhiteSpace(bookmarkInputTextBox?.Text))
            {
                RunBookmarkRouteCalculation();
            }
        }

        private void BookmarkStartOption_Changed(object sender, RoutedEventArgs e)
        {
            if (!bookmarkRoutePanelReady)
            {
                return;
            }

            // Following the map makes the picker meaningless, and leaving it editable would leave two
            // controls claiming to own the start.
            BookmarkStartSystemDropDownAC.IsEnabled = bookmarkStartUseSelectedChk.IsChecked != true;

            UpdateBookmarkStartInfo();

            // A toggle is a deliberate change of the start, unlike a map click while the box is ticked
            // (which only marks the plan stale) or a keystroke in the picker.
            if (!string.IsNullOrWhiteSpace(bookmarkInputTextBox?.Text))
            {
                RunBookmarkRouteCalculation();
            }
        }

        private void SwitchBookmarkRouteCharacter()
        {
            if (!bookmarkRoutePanelReady)
            {
                return;
            }

            // Stash whatever's on screen under the character we're leaving, so it's there if we switch back.
            if (bookmarkRouteCacheOwnerId.HasValue)
            {
                if (bookmarkRouteResult != null)
                {
                    bookmarkRouteCache[bookmarkRouteCacheOwnerId.Value] = (bookmarkRouteStartSystem, bookmarkRouteResult);
                }
                else
                {
                    bookmarkRouteCache.Remove(bookmarkRouteCacheOwnerId.Value);
                }
            }

            bookmarkCalcGeneration++; // any in-flight calculation for the character we're leaving gets discarded when it lands

            EVEData.LocalCharacter character = ActiveCharacter;
            bookmarkRouteCacheOwnerId = character?.ID;

            if (character != null && bookmarkRouteCache.TryGetValue(character.ID, out (string StartSystem, BookmarkRoute Result) cached))
            {
                bookmarkRouteStartSystem = cached.StartSystem;
                RenderBookmarkResult(cached.Result);
            }
            else
            {
                ClearBookmarkRoutePanelDisplay();
            }

            UpdateBookmarkStartInfo();
        }

        private void ClearBookmarkRoutePanelDisplay()
        {
            bookmarkRouteResult = null;
            bookmarkRouteStartSystem = null;
            bookmarkLinesPanel.Children.Clear();
            bookmarkRouteStatusLbl.Content = "";
            bookmarkUnreachableText.Text = "";
            bookmarkIsolatedText.Text = "";
            bookmarkUnparsedText.Text = "";

            // The headers carry the counts, so they'd keep advertising a plan that's gone.
            bookmarkUnreachableGroupBox.Header = GetResourceText("Main_BM_GrpUnreachable", "Unreachable");
            bookmarkIsolatedGroupBox.Header = GetResourceText("Main_BM_GrpIsolated", "Isolated, dropped");
            bookmarkUnparsedGroupBox.Header = GetResourceText("Main_BM_GrpUnparsed", "Unparsed");

            // Clear the map overlay : RegionControl/UniverseControl draw whatever's in these
            // properties, independently of ActiveRoute/CapitalRoute, so they need clearing explicitly.
            RegionUC.BookmarkRoute = null;
            RegionUC.BookmarkRouteStartSystem = null;
            UniverseUC.BookmarkRoute = null;
            UniverseUC.BookmarkRouteStartSystem = null;
            RegionUC.ReDrawMap();
            UniverseUC.ReDrawMap(false, false, true);
        }

        private void ClearBookmarkRouteBtn_Click(object sender, RoutedEventArgs e)
        {
            // Drop the cached copy for this character too : leaving it would restore the very plan this
            // button just cleared the next time the user switches characters away and back.
            if (bookmarkRouteCacheOwnerId.HasValue)
            {
                bookmarkRouteCache.Remove(bookmarkRouteCacheOwnerId.Value);
            }

            // Anything already in flight belongs to the plan being cleared.
            bookmarkCalcGeneration++;

            ClearBookmarkRoutePanelDisplay();
            bookmarkRouteStatusLbl.Content = GetResourceText("Main_BM_MsgCleared", "Planned route cleared.");
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

            string text = bookmarkInputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            // A character is no longer required to plan : the start is whatever the panel says it is,
            // and the character is only one of the places that can come from.
            string startSystem = ResolveBookmarkStartSystem(out _);
            if (string.IsNullOrEmpty(startSystem))
            {
                bookmarkRouteStatusLbl.Content = GetResourceText("Main_BM_MsgNoStart", "No start system : pick one, or log in a character.");
                return;
            }

            if (EVEManager.GetEveSystem(startSystem) == null)
            {
                bookmarkRouteStatusLbl.Content = $"Unknown start system : {startSystem}";
                return;
            }

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
                    // Re-enabled before the superseded check : a calculation that lost the race still owns
                    // the disabled button it set when it started, and nothing else would turn it back on.
                    CalculateBookmarkRouteBtn.IsEnabled = true;

                    if (generation != bookmarkCalcGeneration)
                    {
                        return; // superseded by a newer calculation (e.g. rapid K slider drags)
                    }

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
                : $"Start {bookmarkRouteStartSystem} · {result.Lines.Count} line(s) · {result.PlannedTargets}/{result.TotalTargets} systems · {result.PlannedBookmarks}/{result.TotalBookmarks} bookmarks · {result.BookmarkDiscardRate:P0} discarded";

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
