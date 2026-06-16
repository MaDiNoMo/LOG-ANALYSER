using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using System.Collections.Generic;
using System;
using System.Linq;

namespace LOG_EZ
{
    public partial class MainWindow : Window
    {
        private readonly string filePath = @"C:\Users\ArJuN\OneDrive\Documents\sequencesaver\Sequence.xml";
        private bool showAllData = false;
        private ScrollViewer _actualScrollViewer;
        private ScrollViewer _expectedScrollViewer;
        private bool _isSyncingScroll = false;

        public MainWindow()
        {
            InitializeComponent();
            if (TreeView1 != null) TreeView1.ContextMenu = null;

            EventMapper.LoadEventMapping(@"C:\Users\ArJuN\OneDrive\Documents\project phase 1\EVENTS.txt");

            InitializeSecsPalette();
            InitializeMockDatabase();

            // NEW: Wait for the window to finish drawing, then grab the scroll bars!
            this.Loaded += MainWindow_Loaded;
        }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Force the templates to build
            ActualSequenceTree.ApplyTemplate();
            ExpectedSequenceTree.ApplyTemplate();

            // Get the scrollers by the names we just defined in XAML
            var actualScroll = ActualSequenceTree.Template.FindName("PART_ActualScroller", ActualSequenceTree) as ScrollViewer;
            var expectedScroll = ExpectedSequenceTree.Template.FindName("PART_ExpectedScroller", ExpectedSequenceTree) as ScrollViewer;

            if (actualScroll != null && expectedScroll != null)
            {
                // This links them together at the WPF Engine level
                // Whenever one scrolls, the other mirrors it automatically
                actualScroll.ScrollChanged += (s, args) => {
                    if (Math.Abs(expectedScroll.VerticalOffset - actualScroll.VerticalOffset) > 1.0)
                        expectedScroll.ScrollToVerticalOffset(actualScroll.VerticalOffset);
                };

                expectedScroll.ScrollChanged += (s, args) => {
                    if (Math.Abs(actualScroll.VerticalOffset - expectedScroll.VerticalOffset) > 1.0)
                        actualScroll.ScrollToVerticalOffset(expectedScroll.VerticalOffset);
                };
            }
        }

        // ==========================================
        // SYNCHRONIZED SCROLLING LOGIC
        // ==========================================
        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isSyncingScroll) return;

            var scroller = sender as ScrollViewer;
            if (scroller == null) return;

            _isSyncingScroll = true;
            try
            {
                if (scroller == _actualScrollViewer && _expectedScrollViewer != null)
                {
                    // Sync by percentage to ensure they stay locked even if content varies
                    double ratio = scroller.VerticalOffset / scroller.ScrollableHeight;
                    if (!double.IsNaN(ratio))
                        _expectedScrollViewer.ScrollToVerticalOffset(ratio * _expectedScrollViewer.ScrollableHeight);
                }
                else if (scroller == _expectedScrollViewer && _actualScrollViewer != null)
                {
                    double ratio = scroller.VerticalOffset / scroller.ScrollableHeight;
                    if (!double.IsNaN(ratio))
                        _actualScrollViewer.ScrollToVerticalOffset(ratio * _actualScrollViewer.ScrollableHeight);
                }
            }
            finally { _isSyncingScroll = false; }
        }

        // ==========================================
        // UI TOGGLE HANDLERS & HELPERS
        // ==========================================
        private void Tab2TimeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (Tab2TimeGrid != null && Tab2FilterRadio != null)
                Tab2TimeGrid.Visibility = Tab2FilterRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Tab3TimeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (Tab3TimeGrid != null && Tab3FilterRadio != null)
                Tab3TimeGrid.Visibility = Tab3FilterRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnToggleViewClick(object sender, RoutedEventArgs e)
        {
            if (ViewModeToggle != null)
            {
                showAllData = ViewModeToggle.IsChecked == true;
                ViewModeToggle.Content = showAllData ? "SHOW ONLY S6F11" : "SHOW ALL DATA";
                OnSearchLogsClick(null, null);
            }
        }

        private DateTime CombineDateTime(DateTime? date, string timeStr, bool isEnd)
        {
            DateTime baseDate = date ?? DateTime.Today;
            if (TimeSpan.TryParse(timeStr, out TimeSpan time)) return baseDate.Add(time);
            return isEnd ? baseDate.AddDays(1).AddTicks(-1) : baseDate;
        }

        // ==========================================
        // TAB 2: ANALYSIS ENGINE (DUAL TREES)
        // ==========================================
        private void OnUploadLogClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*";
            openFileDialog.Title = "Select SDR Log File";

            if (openFileDialog.ShowDialog() == true)
            {
                LogPathTextBox.Text = openFileDialog.FileName;
                UpdateDatePickersFromLog(openFileDialog.FileName);
            }
        }

        // ==========================================
        // SYNCHRONIZED DEEP TREE EXPANSION
        // ==========================================
        private bool _isSyncingExpand = false;

        private void SyncTreeNodes(object sender, RoutedEventArgs e)
        {
            if (_isSyncingExpand) return;
            var tvi = sender as TreeViewItem;

            // Only react to the root node itself being clicked
            if (tvi == null || e.OriginalSource != tvi) return;

            _isSyncingExpand = true;
            try
            {
                bool isExpanded = tvi.IsExpanded;

                // 1. Force the clicked side to pop open all nested [Lists]!
                if (isExpanded) tvi.ExpandSubtree();

                // Figure out which tree we need to sync
                TreeView sourceTree = ActualSequenceTree.Items.Contains(tvi) ? ActualSequenceTree : ExpectedSequenceTree;
                TreeView targetTree = sourceTree == ActualSequenceTree ? ExpectedSequenceTree : ActualSequenceTree;

                // 2. Expand the exact same index on the opposite side deeply
                int index = sourceTree.Items.IndexOf(tvi);
                if (index >= 0 && index < targetTree.Items.Count)
                {
                    var targetNode = targetTree.Items[index] as TreeViewItem;
                    if (targetNode != null)
                    {
                        targetNode.IsExpanded = isExpanded;
                        if (isExpanded) targetNode.ExpandSubtree();
                    }
                }
            }
            finally
            {
                _isSyncingExpand = false;
            }
        }

        // ==========================================
        // DYNAMIC DATE PICKER CONSTRAINTS
        // ==========================================
        private void UpdateDatePickersFromLog(string path)
        {
            DateTime? minDate = null;
            DateTime? maxDate = null;

            try
            {
                foreach (string line in File.ReadLines(path))
                {
                    if (line.Length >= 19 && DateTime.TryParse(line.Substring(0, 19), out DateTime dt))
                    {
                        if (minDate == null || dt.Date < minDate) minDate = dt.Date;
                        if (maxDate == null || dt.Date > maxDate) maxDate = dt.Date;
                    }
                }

                if (minDate.HasValue && maxDate.HasValue)
                {
                    Tab2StartDate.DisplayDateStart = Tab3StartDate.DisplayDateStart = minDate.Value;
                    Tab2StartDate.DisplayDateEnd = Tab3StartDate.DisplayDateEnd = maxDate.Value;
                    Tab2EndDate.DisplayDateStart = Tab3EndDate.DisplayDateStart = minDate.Value;
                    Tab2EndDate.DisplayDateEnd = Tab3EndDate.DisplayDateEnd = maxDate.Value;

                    Tab2StartDate.SelectedDate = Tab3StartDate.SelectedDate = minDate.Value;
                    Tab2EndDate.SelectedDate = Tab3EndDate.SelectedDate = maxDate.Value;
                }
            }
            catch { /* Silently skip if reading fails */ }
        }

       
        // ==========================================
        // TAB 2: RUN ANALYSIS
        // ==========================================
        private void OnAnalyseRunClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LogPathTextBox.Text) || AnalysisSequenceComboBox.SelectedItem == null) return;

            ActualSequenceTree.Items.Clear();
            ExpectedSequenceTree.Items.Clear();
            AnalysisSummaryText.Text = "Running C++ Engine...";
            AnalysisSummaryText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#61AFEF"));

            string startT = "", endT = "";
            if (Tab2FilterRadio.IsChecked == true)
            {
                startT = CombineDateTime(Tab2StartDate.SelectedDate, Tab2StartTime.Text, false).ToString("yyyy-MM-dd HH:mm:ss");
                endT = CombineDateTime(Tab2EndDate.SelectedDate, Tab2EndTime.Text, true).ToString("yyyy-MM-dd HH:mm:ss");
            }

            string seqTag = ((ComboBoxItem)AnalysisSequenceComboBox.SelectedItem).Tag.ToString();
            XDocument seqDoc = XDocument.Load(filePath);
            var expectedStringList = new List<string>();

            foreach (var s6f11Node in seqDoc.Root?.Element(seqTag)?.Elements("S6F11") ?? Enumerable.Empty<XElement>())
            {
                string d = s6f11Node.Descendants("Data_ID").FirstOrDefault()?.Value?.Trim() ?? "*";
                string c = s6f11Node.Descendants("CEID").FirstOrDefault()?.Value?.Trim() ?? "*";
                string r = s6f11Node.Descendants("Report_ID").FirstOrDefault()?.Value?.Trim() ?? "*";
                expectedStringList.Add($"{d},{c},{r}");
            }

            string expectedListStr = string.Join(";", expectedStringList);
            if (expectedStringList.Count == 0) return;

            SdrEngine.ResultCallback resCb = (isExpected, ts, d, c, r, colorHex) =>
            {
                TreeViewItem node;

                if (colorHex == "#BLANK")
                {
                    node = CreateBlankNode();
                }
                else
                {
                    var ev = new LogEvent { Timestamp = ts, DataID = d, CEID = c, ReportID = r };
                    node = CreateReadOnlyS6F11Node(ev, colorHex);
                }

                if (isExpected) ExpectedSequenceTree.Items.Add(node);
                else ActualSequenceTree.Items.Add(node);
            };

            SdrEngine.SummaryCallback sumCb = (perfectMatches, totalExpected, extraMessages) =>
            {
                if (perfectMatches == totalExpected && extraMessages == 0)
                {
                    AnalysisSummaryText.Text = $"ANALYSIS COMPLETE | Perfect Strict Sequence! ({perfectMatches} Matches)";
                    AnalysisSummaryText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#98C379"));
                }
                else
                {
                    AnalysisSummaryText.Text = $"ANALYSIS FAILED | Expected: {perfectMatches}/{totalExpected} | Unexpected Extras: {extraMessages}";
                    AnalysisSummaryText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E06C75"));
                }
            };

            try
            {
                SdrEngine.CompareLogSequence(LogPathTextBox.Text, startT, endT, expectedListStr, resCb, sumCb);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"C++ Engine Error:\n\n{ex.Message}", "Fatal Interop Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // NODE GENERATORS
        // ==========================================
        private TreeViewItem CreateBlankNode()
        {
            var rootNode = new TreeViewItem { IsExpanded = false, Focusable = false };

            rootNode.Header = new TextBlock
            {
                Text = "-------------------",
                Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5C6370")),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };

            return rootNode;
        }

        private TreeViewItem CreateReadOnlyS6F11Node(LogEvent ev, string colorHex)
        {
            string eventName = EventMapper.GetEventName(ev.CEID);
            string headerText = string.IsNullOrEmpty(eventName) ? $"S6F11_{ev.CEID}" : $"S6F11_{ev.CEID}_{eventName}";

            var rootNode = new TreeViewItem { IsExpanded = false, Focusable = false };
            rootNode.Expanded += SyncTreeNodes;
            rootNode.Collapsed += SyncTreeNodes;

            rootNode.Header = new TextBlock
            {
                Text = headerText,
                Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex)),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };

            var outerList = new TreeViewItem { Header = "[List]", IsExpanded = false, Focusable = false };
            outerList.Items.Add(CreateReadOnlyTagRow("Data_ID", ev.DataID));
            outerList.Items.Add(CreateReadOnlyTagRow("CEID", ev.CEID));

            var innerList = new TreeViewItem { Header = "[List]", IsExpanded = false, Focusable = false };
            innerList.Items.Add(CreateReadOnlyTagRow("Report_ID", ev.ReportID));

            outerList.Items.Add(innerList);
            rootNode.Items.Add(outerList);

            return rootNode;
        }

        private TreeViewItem CreateReadOnlyTagRow(string tagName, string value)
        {
            var rowItem = new TreeViewItem { IsExpanded = false, Focusable = false };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

            panel.Children.Add(new TextBlock { Text = tagName, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAB, 0xB2, 0xBF)), FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 14, FontWeight = FontWeights.Bold });
            panel.Children.Add(new TextBlock { Text = $" [{value}]", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x98, 0xC3, 0x79)), Margin = new Thickness(10, 0, 0, 0), FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 14, FontWeight = FontWeights.Bold });

            rowItem.Header = panel;
            return rowItem;
        }

        private string ExtractTagValue(TreeViewItem item)
        {
            if (item == null) return "-";
            var panel = item.Header as StackPanel;
            if (panel != null && panel.Children.Count > 1)
            {
                var textBlock = panel.Children[1] as TextBlock;
                return textBlock?.Text.Replace("[", "").Replace("]", "").Trim() ?? "-";
            }
            return "-";
        }

        // ==========================================
        // TAB 3: LOG EXPLORER ENGINE
        // ==========================================
        private void OnSearchLogsClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LogPathTextBox.Text) || !File.Exists(LogPathTextBox.Text))
            {
                if (sender != null) MessageBox.Show("Please load an SDR log file in the ANALYSIS TAB first.", "Missing File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string searchType = SearchTypeComboBox.SelectedItem != null ? ((ComboBoxItem)SearchTypeComboBox.SelectedItem).Content.ToString() : "ANY EVENT";
            string searchValue = SearchValueTextBox.Text.Trim();

            bool applyTimeFilter = Tab3FilterRadio.IsChecked == true;
            DateTime filterStart = DateTime.MinValue, filterEnd = DateTime.MaxValue;
            if (applyTimeFilter)
            {
                filterStart = CombineDateTime(Tab3StartDate.SelectedDate, Tab3StartTime.Text, false);
                filterEnd = CombineDateTime(Tab3EndDate.SelectedDate, Tab3EndTime.Text, true);
            }

            var allEvents = SdrLogParser.ParseLog(LogPathTextBox.Text, applyTimeFilter, filterStart, filterEnd);
            var filteredEvents = allEvents.AsEnumerable();

            if (!showAllData) filteredEvents = filteredEvents.Where(ev => ev.Protocol == "S6F11");

            if (!string.IsNullOrEmpty(searchValue))
            {
                if (searchType == "DATA ID") filteredEvents = filteredEvents.Where(ev => ev.DataID == searchValue);
                else if (searchType == "CEID") filteredEvents = filteredEvents.Where(ev => ev.CEID == searchValue);
                else if (searchType == "REPORT ID") filteredEvents = filteredEvents.Where(ev => ev.ReportID == searchValue);
                else filteredEvents = filteredEvents.Where(ev => ev.DataID == searchValue || ev.CEID == searchValue || ev.ReportID == searchValue);
            }

            var finalResults = filteredEvents.ToList();
            SearchResultsGrid.ItemsSource = finalResults;
            ExploreStatsText.Text = $"Found: {finalResults.Count} matching events";
        }

        // ==========================================
        // AUTO-EXTRACT SEQUENCE FROM LOG FILE
        // ==========================================
        private void BtnAutoExtract_Click(object sender, RoutedEventArgs e)
        {
            Window dialog = new Window
            {
                Title = "Extract Sequence Options",
                Width = 450,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 34, 42)),
                ResizeMode = ResizeMode.NoResize
            };

            StackPanel mainPanel = new StackPanel { Margin = new Thickness(20) };

            // 1. File Upload Section
            TextBlock title1 = new TextBlock { Text = "1. UPLOAD SDR LOG", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(97, 175, 239)), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
            Grid fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox txtFile = new TextBox { IsReadOnly = true, Height = 30, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 44, 52)), Foreground = System.Windows.Media.Brushes.White, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 0, 0) };
            Button btnBrowse = new Button { Content = "BROWSE", Width = 80, Margin = new Thickness(10, 0, 0, 0), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 68, 81)), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold };

            Grid.SetColumn(txtFile, 0);
            Grid.SetColumn(btnBrowse, 1);
            fileGrid.Children.Add(txtFile);
            fileGrid.Children.Add(btnBrowse);

            // 2. Time Section
            TextBlock title2 = new TextBlock { Text = "2. TIME RANGE SELECTION", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(152, 195, 121)), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };

            Grid timeGrid = new Grid { Margin = new Thickness(0, 0, 0, 30) };
            timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            timeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock lblStart = new TextBlock { Text = "START:", Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 10) };
            DatePicker dpStart = new DatePicker { Margin = new Thickness(0, 0, 10, 10), Height = 30 };
            TextBox txtStartTime = new TextBox { Margin = new Thickness(0, 0, 0, 10), Height = 30, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 44, 52)), Foreground = System.Windows.Media.Brushes.White, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 0, 0) };

            TextBlock lblEnd = new TextBlock { Text = "END:", Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            DatePicker dpEnd = new DatePicker { Margin = new Thickness(0, 0, 10, 0), Height = 30 };
            TextBox txtEndTime = new TextBox { Height = 30, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 44, 52)), Foreground = System.Windows.Media.Brushes.White, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 0, 0) };

            Grid.SetRow(lblStart, 0); Grid.SetColumn(lblStart, 0);
            Grid.SetRow(dpStart, 0); Grid.SetColumn(dpStart, 1);
            Grid.SetRow(txtStartTime, 0); Grid.SetColumn(txtStartTime, 2);
            Grid.SetRow(lblEnd, 1); Grid.SetColumn(lblEnd, 0);
            Grid.SetRow(dpEnd, 1); Grid.SetColumn(dpEnd, 1);
            Grid.SetRow(txtEndTime, 1); Grid.SetColumn(txtEndTime, 2);

            timeGrid.Children.Add(lblStart); timeGrid.Children.Add(dpStart); timeGrid.Children.Add(txtStartTime);
            timeGrid.Children.Add(lblEnd); timeGrid.Children.Add(dpEnd); timeGrid.Children.Add(txtEndTime);

            // 3. Save Button
            Button btnSave = new Button { Content = "EXTRACT & SAVE SEQUENCE", Height = 40, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(198, 120, 221)), Foreground = System.Windows.Media.Brushes.Black, FontWeight = FontWeights.Bold };

            Action<string> ApplyFileConstraints = (path) =>
            {
                txtFile.Text = path;
                DateTime? minDate = null; DateTime? maxDate = null;
                try
                {
                    foreach (string line in File.ReadLines(path))
                    {
                        if (line.Length >= 19 && DateTime.TryParse(line.Substring(0, 19), out DateTime dt))
                        {
                            if (minDate == null || dt < minDate) minDate = dt;
                            if (maxDate == null || dt > maxDate) maxDate = dt;
                        }
                    }
                    if (minDate.HasValue && maxDate.HasValue)
                    {
                        dpStart.DisplayDateStart = dpEnd.DisplayDateStart = minDate.Value.Date;
                        dpStart.DisplayDateEnd = dpEnd.DisplayDateEnd = maxDate.Value.Date;
                        dpStart.SelectedDate = minDate.Value.Date;
                        dpEnd.SelectedDate = maxDate.Value.Date;
                        txtStartTime.Text = minDate.Value.ToString("HH:mm:ss");
                        txtEndTime.Text = maxDate.Value.ToString("HH:mm:ss");
                    }
                }
                catch { }
            };

            if (!string.IsNullOrWhiteSpace(LogPathTextBox.Text) && File.Exists(LogPathTextBox.Text))
            {
                ApplyFileConstraints(LogPathTextBox.Text);
            }

            btnBrowse.Click += (s, ev) =>
            {
                OpenFileDialog ofd = new OpenFileDialog { Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*" };
                if (ofd.ShowDialog() == true)
                {
                    ApplyFileConstraints(ofd.FileName);
                }
            };

            btnSave.Click += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(txtFile.Text) || !File.Exists(txtFile.Text))
                {
                    MessageBox.Show("Please select a valid log file.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    DateTime start = CombineDateTime(dpStart.SelectedDate, txtStartTime.Text, false);
                    DateTime end = CombineDateTime(dpEnd.SelectedDate, txtEndTime.Text, true);

                    var extractedEvents = SdrLogParser.ParseLog(txtFile.Text, true, start, end)
                                          .Where(x => x.Protocol == "S6F11").ToList();

                    if (extractedEvents.Count == 0)
                    {
                        MessageBox.Show("No S6F11 triplets found in this time range.", "Extraction Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    XDocument doc;
                    if (File.Exists(filePath))
                    {
                        try { doc = XDocument.Load(filePath); } catch { doc = new XDocument(new XElement("SequenceRoot")); }
                    }
                    else doc = new XDocument(new XElement("SequenceRoot"));
                    if (doc.Root == null) doc.Add(new XElement("SequenceRoot"));

                    int seqNum = 1;
                    while (doc.Root.Elements().Any(e => e.Name.LocalName == $"Sequence_Auto_EXTRACTED_SEQUENCE_{seqNum}")) seqNum++;

                    string autoSeqName = $"Sequence_Auto_EXTRACTED_SEQUENCE_{seqNum}";
                    XElement newSequenceNode = new XElement(autoSeqName);

                    foreach (var parsedEv in extractedEvents)
                    {
                        XElement s6f11Node = new XElement("S6F11");
                        string eventName = EventMapper.GetEventName(parsedEv.CEID);
                        string attrName = string.IsNullOrEmpty(eventName) ? parsedEv.CEID : $"{parsedEv.CEID}_{eventName}";
                        s6f11Node.SetAttributeValue("NAME", attrName);

                        XElement outerList = new XElement("List");
                        outerList.Add(new XElement("Data_ID", parsedEv.DataID));
                        outerList.Add(new XElement("CEID", parsedEv.CEID));
                        XElement innerList = new XElement("List");
                        innerList.Add(new XElement("Report_ID", parsedEv.ReportID));
                        outerList.Add(innerList);
                        s6f11Node.Add(outerList);
                        newSequenceNode.Add(s6f11Node);
                    }

                    doc.Root.Add(newSequenceNode);
                    doc.Save(filePath);

                    InitializeMockDatabase();
                    dialog.Close();

                    MessageBox.Show($"Successfully Extracted {extractedEvents.Count} events!\n\nSaved as: EXTRACTED SEQUENCE {seqNum}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Extraction error: {ex.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            mainPanel.Children.Add(title1);
            mainPanel.Children.Add(fileGrid);
            mainPanel.Children.Add(title2);
            mainPanel.Children.Add(timeGrid);
            mainPanel.Children.Add(btnSave);

            dialog.Content = mainPanel;
            dialog.ShowDialog();
        }

        // ==========================================
        // TREEVIEW & XML MANAGEMENT
        // ==========================================
        private void InitializeSecsPalette()
        {
            TreeView2.Items.Clear();
            var root = new TreeViewItem { Header = "SECS Stream Functions", IsExpanded = true };
            var s6f11 = new TreeViewItem { Header = "S6F11", IsExpanded = false };

            var list1 = new TreeViewItem { Header = "[List]", IsExpanded = false };
            list1.Items.Add(new TreeViewItem { Header = "Data_ID", IsExpanded = false });
            list1.Items.Add(new TreeViewItem { Header = "CEID", IsExpanded = false });

            var list2 = new TreeViewItem { Header = "[List]", IsExpanded = false };
            list2.Items.Add(new TreeViewItem { Header = "Report_ID", IsExpanded = false });

            list1.Items.Add(list2);
            s6f11.Items.Add(list1);
            root.Items.Add(s6f11);

            TreeView2.Items.Add(root);
        }

        private void InitializeMockDatabase()
        {
            SequenceListBox.Items.Clear();
            AnalysisSequenceComboBox.Items.Clear();

            try
            {
                if (!File.Exists(filePath)) return;

                XDocument doc = XDocument.Load(filePath);
                if (doc.Root != null)
                {
                    int seqCounter = 1;

                    foreach (XElement sequenceNode in doc.Root.Elements())
                    {
                        string seqTag = sequenceNode.Name.LocalName;
                        string displayEventName = "";
                        bool hasEventName = false;

                        var parts = seqTag.Split(new[] { '_' }, 3);
                        if (parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]))
                        {
                            displayEventName = parts[2].Replace("_", " ");
                            hasEventName = true;
                        }

                        if (!hasEventName)
                        {
                            string seqNumber = parts.Length >= 2 ? parts[1] : "X";
                            displayEventName = $"Sequence {seqNumber}";
                        }

                        string numberedDisplay = $"{seqCounter}. {displayEventName}";

                        var listItem = new ListBoxItem { Content = numberedDisplay, Tag = seqTag, FontWeight = FontWeights.Bold };
                        var ctxMenu = CreateDarkContextMenu();
                        var renameItem = new MenuItem { Header = "Rename Sequence Event..." };
                        renameItem.Click += (s, e) => RenameLeftSequence(seqTag, displayEventName);

                        var deleteItem = new MenuItem { Header = "Delete Sequence", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x6C, 0x75)), FontWeight = FontWeights.Bold };
                        deleteItem.Click += (s, e) => DeleteLeftSequence(seqTag);

                        ctxMenu.Items.Add(renameItem);
                        ctxMenu.Items.Add(new Separator { Background = System.Windows.Media.Brushes.DimGray });
                        ctxMenu.Items.Add(deleteItem);
                        listItem.ContextMenu = ctxMenu;
                        SequenceListBox.Items.Add(listItem);

                        AnalysisSequenceComboBox.Items.Add(new ComboBoxItem { Content = numberedDisplay, Tag = seqTag });

                        seqCounter++;
                    }

                    if (AnalysisSequenceComboBox.Items.Count > 0)
                        AnalysisSequenceComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error reading XML registry: {ex.Message}", "Load Error"); }
        }

        private void SequenceListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selectedItem = SequenceListBox.SelectedItem as ListBoxItem;
            if (selectedItem == null || !File.Exists(filePath)) return;

            TreeView1.Items.Clear();
            string rawSelectedTag = selectedItem.Tag.ToString();

            try
            {
                XDocument doc = XDocument.Load(filePath);
                XElement targetSequence = doc.Root?.Element(rawSelectedTag);

                if (targetSequence != null)
                {
                    var visualRoot = CreateSequenceRootNode(targetSequence.Name.LocalName);
                    LoadXmlElementsRecursively(targetSequence, visualRoot);
                    TreeView1.Items.Add(visualRoot);
                }
            }
            catch (Exception ex) { MessageBox.Show($"Error loading sequence: {ex.Message}", "Parse Error"); }
        }

        private void LoadXmlElementsRecursively(XElement currentXmlElement, TreeViewItem visualParentItem)
        {
            foreach (XElement childElement in currentXmlElement.Elements())
            {
                bool isList = childElement.Name.LocalName == "List" || childElement.Name.LocalName == "[List]";

                if (isList || childElement.HasElements)
                {
                    string headerText = childElement.Name.LocalName;
                    if (headerText.StartsWith("S6F11") && childElement.Attribute("NAME") != null)
                    {
                        string nameVal = childElement.Attribute("NAME").Value;
                        headerText = nameVal.StartsWith("S6F11_") ? nameVal : $"S6F11_{nameVal}";
                    }

                    var folderNode = isList ? CreateListNode(false) : CreateFunctionNode(headerText, false);
                    visualParentItem.Items.Add(folderNode);
                    LoadXmlElementsRecursively(childElement, folderNode);
                }
                else
                {
                    visualParentItem.Items.Add(CreateXmlTagRow(childElement.Name.LocalName, childElement.Value));
                }
            }
        }

        private ContextMenu CreateDarkContextMenu()
        {
            return new ContextMenu
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x28, 0x2C, 0x34)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x44, 0x51)),
                BorderThickness = new Thickness(1)
            };
        }

        private TreeViewItem CreateSequenceRootNode(string headerText)
        {
            var node = new TreeViewItem { Header = headerText, IsExpanded = true };
            var menu = CreateDarkContextMenu();

            Action renameAction = () =>
            {
                var parts = node.Header.ToString().Split(new[] { '_' }, 3);
                string baseName = parts.Length >= 2 ? $"{parts[0]}_{parts[1]}" : node.Header.ToString();
                string currentEventName = parts.Length == 3 ? parts[2].Replace("_", " ") : "";

                var dlg = new InputDialog("Enter Event Name:", currentEventName) { Owner = this };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Answer))
                {
                    node.Header = $"{baseName}_{dlg.Answer.Replace(" ", "_")}";
                }
            };

            var miEvent = new MenuItem { Header = "Set Event Name", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x98, 0xC3, 0x79)) };
            miEvent.Click += (s, e) => renameAction();
            menu.Items.Add(miEvent);
            node.ContextMenu = menu;

            node.MouseDoubleClick += (s, e) =>
            {
                if (!node.IsSelected) return;
                e.Handled = true;
                renameAction();
            };

            return node;
        }

        private TreeViewItem CreateFunctionNode(string headerText, bool isExpanded = true)
        {
            var node = new TreeViewItem { Header = headerText, IsExpanded = isExpanded };
            var menu = CreateDarkContextMenu();

            var miList = new MenuItem { Header = "Add [List]" };
            miList.Click += (s, e) => { node.Items.Add(CreateListNode(true)); node.IsExpanded = true; };

            menu.Items.Add(miList);
            node.ContextMenu = menu;
            return node;
        }

        private TreeViewItem CreateListNode(bool isExpanded = false)
        {
            var node = new TreeViewItem { Header = "[List]", IsExpanded = isExpanded };
            var menu = CreateDarkContextMenu();

            var miItem = new MenuItem { Header = "Add Item" };
            miItem.Click += (s, e) =>
            {
                var dlg = new ItemDialog("", "0") { Owner = this };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ItemName))
                {
                    node.Items.Add(CreateXmlTagRow(dlg.ItemName.Replace(" ", "_"), dlg.ItemValue));
                    node.IsExpanded = true;
                }
            };

            var miList = new MenuItem { Header = "Add [List]" };
            miList.Click += (s, e) => { node.Items.Add(CreateListNode(true)); node.IsExpanded = true; };

            menu.Items.Add(miItem);
            menu.Items.Add(miList);
            node.ContextMenu = menu;
            return node;
        }

        private TreeViewItem CreateXmlTagRow(string tagName, string value)
        {
            var rowItem = new TreeViewItem { IsExpanded = false, Tag = "TagLeaf" };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2), Background = System.Windows.Media.Brushes.Transparent };

            panel.Children.Add(new TextBlock
            {
                Text = tagName,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAB, 0xB2, 0xBF)),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });

            panel.Children.Add(new TextBlock
            {
                Text = $" [{value}]",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x98, 0xC3, 0x79)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });

            rowItem.Header = panel;

            rowItem.MouseDoubleClick += (s, e) =>
            {
                if (!rowItem.IsSelected) return;
                e.Handled = true;

                var currentName = ((TextBlock)panel.Children[0]).Text.Trim();
                var currentVal = ((TextBlock)panel.Children[1]).Text.Replace("[", "").Replace("]", "").Trim();

                var dlg = new ItemDialog(currentName, currentVal) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    ((TextBlock)panel.Children[0]).Text = dlg.ItemName.Replace(" ", "_").Replace("(", "_").Replace(")", "_");
                    ((TextBlock)panel.Children[1]).Text = $" [{dlg.ItemValue}]";
                }
            };

            return rowItem;
        }

        private void TreeView2_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedPalette = TreeView2.SelectedItem as TreeViewItem;
            if (selectedPalette == null || selectedPalette.Header.ToString().Contains("Stream Functions")) return;

            string headerText = selectedPalette.Header.ToString();
            bool isItem = selectedPalette.Items.Count == 0;
            bool isList = headerText.Contains("[L]") || headerText.Contains("[List]");
            bool isFunction = !isItem && !isList && !headerText.Contains("Stream Functions");

            string cleanName = headerText;
            if (isList) cleanName = "[List]";
            else if (isFunction) cleanName = headerText.Split('-')[0].Trim();

            if (TreeView1.Items.Count == 0)
            {
                if (!isFunction)
                {
                    MessageBox.Show("Please click a Stream Function (like S6F11) to start a new sequence!", "Start Sequence", MessageBoxButton.OK, MessageBoxImage.Information);
                    selectedPalette.IsSelected = false;
                    return;
                }

                int nextSeq = SequenceListBox.Items.Count + 1;
                var seqRoot = CreateSequenceRootNode($"Sequence_{nextSeq}");
                var funcNode = CreateFunctionNode(cleanName, true);

                seqRoot.Items.Add(funcNode);
                TreeView1.Items.Add(seqRoot);

                funcNode.IsSelected = true;
                selectedPalette.IsSelected = false;
                return;
            }

            var targetNode = TreeView1.SelectedItem as TreeViewItem;
            if (targetNode == null)
            {
                var root = TreeView1.Items[0] as TreeViewItem;
                targetNode = root?.Items.Count > 0 ? root.Items[0] as TreeViewItem : root;
            }

            if (targetNode?.Tag?.ToString() == "TagLeaf") targetNode = targetNode.Parent as TreeViewItem;
            if (targetNode == null) return;

            if (isFunction)
            {
                var root = TreeView1.Items[0] as TreeViewItem;
                var funcNode = CreateFunctionNode(cleanName, true);
                root.Items.Add(funcNode);
                funcNode.IsSelected = true;
            }
            else if (isList)
            {
                var listNode = CreateListNode(true);
                targetNode.Items.Add(listNode);
                listNode.IsSelected = true;
                targetNode.IsExpanded = true;
            }
            else if (isItem)
            {
                AddSmartItemToNode(targetNode, cleanName, "0");
            }

            selectedPalette.IsSelected = false;
        }

        private void AddSmartItemToNode(TreeViewItem targetNode, string itemName, string itemValue)
        {
            var newItem = CreateXmlTagRow(itemName, itemValue);
            if (targetNode.Header.ToString() == "[List]")
            {
                targetNode.Items.Add(newItem);
                targetNode.IsExpanded = true;
            }
            else
            {
                TreeViewItem lastList = null;
                foreach (TreeViewItem child in targetNode.Items)
                {
                    if (child.Header.ToString() == "[List]") lastList = child;
                }
                if (lastList == null)
                {
                    lastList = CreateListNode(true);
                    targetNode.Items.Add(lastList);
                }
                lastList.Items.Add(newItem);
                lastList.IsExpanded = true;
                targetNode.IsExpanded = true;
            }
        }

        private void DeleteLeftSequence(string sequenceTag)
        {
            if (MessageBox.Show($"Are you sure you want to delete '{sequenceTag}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                XDocument doc = XDocument.Load(filePath);
                doc.Root?.Element(sequenceTag)?.Remove();
                doc.Save(filePath);
                TreeView1.Items.Clear();
                InitializeMockDatabase();
            }
        }

        private void RenameLeftSequence(string sequenceTag, string oldEventName)
        {
            if (oldEventName.Contains("NO EVENT NAME")) oldEventName = "";

            var dlg = new InputDialog("Enter New Event Name:", oldEventName) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Answer))
            {
                XDocument doc = XDocument.Load(filePath);
                XElement targetSequence = doc.Root?.Element(sequenceTag);
                if (targetSequence != null)
                {
                    var parts = targetSequence.Name.LocalName.Split(new[] { '_' }, 3);
                    string baseName = parts.Length >= 2 ? $"{parts[0]}_{parts[1]}" : targetSequence.Name.LocalName;
                    targetSequence.Name = $"{baseName}_{dlg.Answer.Replace(" ", "_")}";

                    doc.Save(filePath);
                    TreeView1.Items.Clear();
                    InitializeMockDatabase();
                }
            }
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e) => TreeView1.Items.Clear();
        private void BtnClear_Click(object sender, RoutedEventArgs e) => TreeView1.Items.Clear();

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TreeView1.SelectedItem as TreeViewItem;
            if (selected == null) return;
            var parent = selected.Parent as TreeViewItem;
            if (parent != null) parent.Items.Remove(selected);
            else TreeView1.Items.Remove(selected);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (TreeView1.Items.Count == 0) return;
            var rootItem = TreeView1.Items[0] as TreeViewItem;
            if (rootItem?.Header == null) return;

            try
            {
                XDocument doc;
                if (File.Exists(filePath))
                {
                    try { doc = XDocument.Load(filePath); } catch { doc = new XDocument(new XElement("SequenceRoot")); }
                }
                else doc = new XDocument(new XElement("SequenceRoot"));

                if (doc.Root == null) doc.Add(new XElement("SequenceRoot"));

                string currentSequenceTagName = rootItem.Header.ToString().Replace(" ", "_");
                XElement cleanUpdatedBranch = new XElement(currentSequenceTagName);
                SerializeUiToXmlElement(rootItem, cleanUpdatedBranch);

                foreach (XElement node in cleanUpdatedBranch.Descendants().ToList())
                {
                    if (node.Name.LocalName.StartsWith("S6F11"))
                    {
                        var ceidNode = node.Descendants("CEID").FirstOrDefault();
                        string ceidVal = ceidNode != null ? ceidNode.Value : "*";

                        string eventName = EventMapper.GetEventName(ceidVal);

                        string attrName = string.IsNullOrEmpty(eventName) ? ceidVal : $"{ceidVal}_{eventName}";

                        node.Name = "S6F11";
                        node.SetAttributeValue("NAME", attrName);
                    }
                }

                XElement existingNodeMatch = doc.Root.Element(currentSequenceTagName);
                if (existingNodeMatch != null) existingNodeMatch.ReplaceWith(cleanUpdatedBranch);
                else doc.Root.Add(cleanUpdatedBranch);

                doc.Save(filePath);
                InitializeMockDatabase();

                foreach (ListBoxItem item in SequenceListBox.Items)
                {
                    if (item.Tag.ToString() == currentSequenceTagName) { SequenceListBox.SelectedItem = item; break; }
                }
                MessageBox.Show("Sequence Configuration Saved Successfully!", "Sync Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show($"File generation error: {ex.Message}", "Save Failure"); }
        }

        private void BtnViewXml_Click(object sender, RoutedEventArgs e)
        {
            if (TreeView1.Items.Count == 0)
            {
                MessageBox.Show("No sequence loaded to view.", "View XML", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var rootItem = TreeView1.Items[0] as TreeViewItem;
                if (rootItem?.Header == null) { MessageBox.Show("Invalid sequence.", "View XML", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                string currentSequenceTagName = rootItem.Header.ToString().Replace(" ", "_");
                XElement cleanUpdatedBranch = new XElement(currentSequenceTagName);
                SerializeUiToXmlElement(rootItem, cleanUpdatedBranch);

                var doc = new XDocument(cleanUpdatedBranch);
                MessageBox.Show(doc.ToString(), "Sequence XML Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate XML preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SerializeUiToXmlElement(TreeViewItem parentUiNode, XElement parentXmlNode)
        {
            foreach (TreeViewItem childItem in parentUiNode.Items)
            {
                if (childItem.Tag?.ToString() == "TagLeaf")
                {
                    var panel = childItem.Header as StackPanel;
                    var tagNameBlock = panel?.Children[0] as TextBlock;
                    var valueBlock = panel?.Children[1] as TextBlock;

                    if (tagNameBlock != null && valueBlock != null)
                    {
                        string tagName = tagNameBlock.Text.Trim().Replace(" ", "_");
                        string textValue = valueBlock.Text.Replace("[", "").Replace("]", "").Trim();
                        parentXmlNode.Add(new XElement(tagName, textValue));
                    }
                }
                else
                {
                    string folderTagName = childItem.Header?.ToString().Replace("[", "").Replace("]", "").Replace(" ", "_") ?? "Folder";
                    XElement innerFolderXmlNode = new XElement(folderTagName);
                    parentXmlNode.Add(innerFolderXmlNode);
                    SerializeUiToXmlElement(childItem, innerFolderXmlNode);
                }
            }
        }

        // ==========================================
        // EXPORT REPORT ENGINE
        // ==========================================
        private void OnSaveReportClick(object sender, RoutedEventArgs e)
        {
            if (ExpectedSequenceTree.Items.Count == 0)
            {
                MessageBox.Show("Please run a Log Analysis first before exporting a report.", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "CSV Excel File (*.csv)|*.csv|Text File (*.txt)|*.txt";
            saveFileDialog.Title = "Export Analysis Report";
            saveFileDialog.FileName = $"Sequence_Report_{DateTime.Now:MMdd_HHmm}.csv";

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName))
                    {
                        writer.WriteLine("LOG ANALYZER 4.0 - SEQUENCE REPORT");
                        writer.WriteLine($"Generated on:,{DateTime.Now}");
                        writer.WriteLine($"Target Sequence:,{((ComboBoxItem)AnalysisSequenceComboBox.SelectedItem)?.Content.ToString().Replace(",", " ")}");
                        writer.WriteLine($"Overall Result:,{AnalysisSummaryText.Text}");
                        writer.WriteLine("");
                        writer.WriteLine("STATUS,EVENT NAME,DATA_ID,CEID,REPORT_ID");

                        foreach (TreeViewItem rootNode in ExpectedSequenceTree.Items)
                        {
                            var headerBlock = rootNode.Header as TextBlock;
                            string nodeText = headerBlock?.Text.Replace(",", " ") ?? "Unknown";
                            string colorHex = headerBlock?.Foreground.ToString() ?? "";

                            string status = "FATAL (MISSING)";
                            if (colorHex.Contains("98C379")) status = "MATCHED (PERFECT)";
                            else if (colorHex.Contains("E06C75"))
                            {
                                bool foundInActual = false;
                                foreach (TreeViewItem actualNode in ActualSequenceTree.Items)
                                {
                                    var actHeader = actualNode.Header as TextBlock;
                                    if (actHeader != null && actHeader.Text == headerBlock.Text && actHeader.Foreground.ToString().Contains("E06C75"))
                                    {
                                        foundInActual = true;
                                        break;
                                    }
                                }
                                status = foundInActual ? "FAILED (OUT OF ORDER)" : "FATAL (MISSING)";
                            }

                            string dataId = "-", ceid = "-", repId = "-";
                            if (rootNode.Items.Count > 0 && rootNode.Items[0] is TreeViewItem outerList)
                            {
                                dataId = ExtractTagValue(outerList.Items[0] as TreeViewItem);
                                ceid = ExtractTagValue(outerList.Items[1] as TreeViewItem);

                                if (outerList.Items.Count > 2 && outerList.Items[2] is TreeViewItem innerList)
                                {
                                    repId = ExtractTagValue(innerList.Items[0] as TreeViewItem);
                                }
                            }

                            writer.WriteLine($"{status},{nodeText},{dataId},{ceid},{repId}");
                        }
                    }
                    MessageBox.Show("Report exported successfully!\n\nYou can open this file in Excel.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving report: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}