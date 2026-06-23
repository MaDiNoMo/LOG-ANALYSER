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
        private bool _isSyncingExpand = false;

        private ScrollViewer _actualScroll;
        private ScrollViewer _expectedScroll;

        // ==========================================
        // UNIVERSAL LOG EVENT DATA CLASS
        // ==========================================
        public class LogEvent
        {
            public string Timestamp { get; set; }
            public string Protocol { get; set; }
            public int LineNumber { get; set; }

            // S6F11 Properties
            public string DataID { get; set; }
            public string CEID { get; set; }
            public string ReportID { get; set; }

            // S3F17 Properties
            public string CarrierAction { get; set; }
            public string CarrierID { get; set; }
            public string PortID { get; set; }
            public string AttrID { get; set; }
            public string AttrData { get; set; }

            // S4F17 Properties
            public string SpoolID { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            if (TreeView1 != null) TreeView1.ContextMenu = null;

            // Failsafe in case EVENTS.txt doesn't exist on another computer
            try { EventMapper.LoadEventMapping(@"C:\Users\ArJuN\OneDrive\Documents\project phase 1\EVENTS.txt"); } catch { }

            InitializeSecsPalette();
            InitializeMockDatabase();

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ActualSequenceTree.ApplyTemplate();
            ExpectedSequenceTree.ApplyTemplate();

            _actualScroll = ActualSequenceTree.Template.FindName("PART_ActualScroller", ActualSequenceTree) as ScrollViewer;
            _expectedScroll = ExpectedSequenceTree.Template.FindName("PART_ExpectedScroller", ExpectedSequenceTree) as ScrollViewer;

            if (_actualScroll != null && _expectedScroll != null)
            {
                _actualScroll.ScrollChanged += (s, args) => {
                    if (Math.Abs(_expectedScroll.VerticalOffset - _actualScroll.VerticalOffset) > 1.0)
                        _expectedScroll.ScrollToVerticalOffset(_actualScroll.VerticalOffset);
                };

                _expectedScroll.ScrollChanged += (s, args) => {
                    if (Math.Abs(_actualScroll.VerticalOffset - _expectedScroll.VerticalOffset) > 1.0)
                        _actualScroll.ScrollToVerticalOffset(_expectedScroll.VerticalOffset);
                };
            }
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
                ViewModeToggle.Content = showAllData ? "SHOW ONLY TARGET PROTOCOLS" : "SHOW ALL DATA";
                OnSearchLogsClick(null, null);
            }
        }

        private DateTime CombineDateTime(DateTime? date, string timeStr, bool isEnd)
        {
            DateTime baseDate = date ?? DateTime.Today;
            if (TimeSpan.TryParse(timeStr, out TimeSpan time)) return baseDate.Add(time);
            return isEnd ? baseDate.AddDays(1).AddTicks(-1) : baseDate;
        }

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

        private void SyncTreeNodes(object sender, RoutedEventArgs e)
        {
            if (_isSyncingExpand) return;
            var tvi = sender as TreeViewItem;

            if (tvi == null || e.OriginalSource != tvi) return;

            _isSyncingExpand = true;
            try
            {
                bool isExpanded = tvi.IsExpanded;
                if (isExpanded) tvi.ExpandSubtree();

                TreeView sourceTree = ActualSequenceTree.Items.Contains(tvi) ? ActualSequenceTree : ExpectedSequenceTree;
                TreeView targetTree = sourceTree == ActualSequenceTree ? ExpectedSequenceTree : ActualSequenceTree;

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
            catch { }
        }

        // ==========================================
        // NODE GENERATORS
        // ==========================================
        private TreeViewItem CreateBlankNode()
        {
            var rootNode = new TreeViewItem { IsExpanded = false, Focusable = false };
            rootNode.Header = new TextBlock
            {
                Text = "--------",
                Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5C6370")),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };
            return rootNode;
        }
        private TreeViewItem CreateGhostNode(TreeViewItem sourceNode)
        {
            if (sourceNode == null) return new TreeViewItem();

            // 🌟 Set Opacity to 0.4 to make the entire node and its colors faded/light!
            var ghost = new TreeViewItem { IsExpanded = sourceNode.IsExpanded, Focusable = false, Tag = "GhostNode", Opacity = 0.4 };

            if (sourceNode.Header is StackPanel originalPanel)
            {
                var ghostPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

                var origNameBlock = originalPanel.Children[0] as TextBlock;
                if (origNameBlock != null)
                    ghostPanel.Children.Add(new TextBlock { Text = origNameBlock.Text, Foreground = origNameBlock.Foreground, VerticalAlignment = VerticalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 14, FontWeight = FontWeights.Bold });

                if (originalPanel.Children.Count > 1 && originalPanel.Children[1] is TextBlock origValBlock)
                {
                    ghostPanel.Children.Add(new TextBlock { Text = origValBlock.Text, Foreground = origValBlock.Foreground, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 14, FontWeight = FontWeights.Bold });
                }
                ghost.Header = ghostPanel;
            }
            else if (sourceNode.Header is TextBlock originalBlock)
            {
                ghost.Header = new TextBlock { Text = originalBlock.Text, Foreground = originalBlock.Foreground, VerticalAlignment = VerticalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4) };
            }
            else ghost.Header = new TextBlock { Text = sourceNode.Header?.ToString() };

            foreach (TreeViewItem child in sourceNode.Items) ghost.Items.Add(CreateGhostNode(child));

            return ghost;
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
            outerList.Items.Add(CreateXmlTagRow("Data_ID", ev.DataID ?? "0"));
            outerList.Items.Add(CreateXmlTagRow("CEID", ev.CEID ?? "0"));

            var innerList = new TreeViewItem { Header = "[List]", IsExpanded = false, Focusable = false };
            innerList.Items.Add(CreateXmlTagRow("Report_ID", ev.ReportID ?? "0"));

            outerList.Items.Add(innerList);
            rootNode.Items.Add(outerList);

            return rootNode;
        }

        private TreeViewItem CreateReadOnlyS3F17Node(LogEvent ev, string colorHex)
        {
            string headerText = $"S3F17_{ev.CarrierAction}";

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
            outerList.Items.Add(CreateXmlTagRow("DATAID", ev.DataID ?? "0"));
            outerList.Items.Add(CreateXmlTagRow("CARRIERACTION", ev.CarrierAction ?? "0"));
            outerList.Items.Add(CreateXmlTagRow("CARRIERID", ev.CarrierID ?? "0"));
            outerList.Items.Add(CreateXmlTagRow("PORTID", ev.PortID ?? "0"));

            var innerList1 = new TreeViewItem { Header = "[List]", IsExpanded = false, Focusable = false };
            var innerList2 = new TreeViewItem { Header = "[List]", IsExpanded = false, Focusable = false };
            innerList2.Items.Add(CreateXmlTagRow("ATTRID", ev.AttrID ?? "-"));
            innerList2.Items.Add(CreateXmlTagRow("ATTRDATA", ev.AttrData ?? "-"));

            innerList1.Items.Add(innerList2);
            outerList.Items.Add(innerList1);
            rootNode.Items.Add(outerList);

            return rootNode;
        }

        private TreeViewItem CreateReadOnlyS4F17Node(LogEvent ev, string colorHex)
        {
            string headerText = $"S4F17_{ev.SpoolID}";

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
            outerList.Items.Add(CreateXmlTagRow("Spool_ID", ev.SpoolID ?? "0"));

            rootNode.Items.Add(outerList);
            return rootNode;
        }

        //private TreeViewItem CreateXmlTagRow(string tagName, string value)
        //{
        //    var rowItem = new TreeViewItem { IsExpanded = false, Tag = "TagLeaf" };
        //    var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2), Background = System.Windows.Media.Brushes.Transparent };

        //    panel.Children.Add(new TextBlock
        //    {
        //        Text = tagName,
        //        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAB, 0xB2, 0xBF)),
        //        VerticalAlignment = VerticalAlignment.Center,
        //        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        //        FontSize = 14,
        //        FontWeight = FontWeights.Bold
        //    });

        //    panel.Children.Add(new TextBlock
        //    {
        //        Text = $" [{value}]",
        //        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x98, 0xC3, 0x79)),
        //        VerticalAlignment = VerticalAlignment.Center,
        //        Margin = new Thickness(10, 0, 0, 0),
        //        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        //        FontSize = 14,
        //        FontWeight = FontWeights.Bold
        //    });

        //    rowItem.Header = panel;

        //    rowItem.MouseDoubleClick += (s, e) =>
        //    {
        //        if (!rowItem.IsSelected) return;
        //        e.Handled = true;

        //        var currentName = ((TextBlock)panel.Children[0]).Text.Trim();
        //        var currentVal = ((TextBlock)panel.Children[1]).Text.Replace("[", "").Replace("]", "").Trim();

        //        var dlg = new ItemDialog(currentName, currentVal) { Owner = this };
        //        if (dlg.ShowDialog() == true)
        //        {
        //            ((TextBlock)panel.Children[0]).Text = dlg.ItemName.Replace(" ", "_").Replace("(", "_").Replace(")", "_");
        //            ((TextBlock)panel.Children[1]).Text = $" [{dlg.ItemValue}]";
        //        }
        //    };

        //    return rowItem;
        //}

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
        // THE ANALYSIS ENGINE (UNIFIED & SMART)
        // ==========================================
        // ==========================================
        // THE ANALYSIS ENGINE (100% C# - C++ BYPASSED)
        // ==========================================
        private void OnAnalyseRunClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LogPathTextBox.Text) || AnalysisSequenceComboBox.SelectedItem == null) return;

            ActualSequenceTree.Items.Clear();
            ExpectedSequenceTree.Items.Clear();

            string seqTag = ((ComboBoxItem)AnalysisSequenceComboBox.SelectedItem).Tag.ToString();
            XDocument seqDoc = XDocument.Load(filePath);

            // 1. POPULATE EXPECTED SEQUENCE DIRECTLY FROM XML
            foreach (var node in seqDoc.Root?.Element(seqTag)?.Elements() ?? Enumerable.Empty<XElement>())
            {
                string msgType = node.Name.LocalName;
                TreeViewItem tvNode = null;

                if (msgType == "S6F11")
                {
                    var ev = new LogEvent
                    {
                        Protocol = "S6F11",
                        DataID = node.Descendants("Data_ID").FirstOrDefault()?.Value?.Trim() ?? "",
                        CEID = node.Descendants("CEID").FirstOrDefault()?.Value?.Trim() ?? "",
                        ReportID = node.Descendants("Report_ID").FirstOrDefault()?.Value?.Trim() ?? ""
                    };
                    tvNode = CreateReadOnlyS6F11Node(ev, "#ABB2BF");
                }
                else if (msgType == "S3F17")
                {
                    var ev = new LogEvent
                    {
                        Protocol = "S3F17",
                        DataID = node.Descendants("DATAID").FirstOrDefault()?.Value?.Trim() ?? "",
                        CarrierAction = node.Descendants("CARRIERACTION").FirstOrDefault()?.Value?.Trim() ?? "",
                        CarrierID = node.Descendants("CARRIERID").FirstOrDefault()?.Value?.Trim() ?? "",
                        PortID = node.Descendants("PORTID").FirstOrDefault()?.Value?.Trim() ?? ""
                    };
                    tvNode = CreateReadOnlyS3F17Node(ev, "#ABB2BF");
                }
                else if (msgType == "S4F17")
                {
                    var ev = new LogEvent
                    {
                        Protocol = "S4F17",
                        SpoolID = node.Descendants("Spool_ID").FirstOrDefault()?.Value?.Trim() ?? ""
                    };
                    tvNode = CreateReadOnlyS4F17Node(ev, "#ABB2BF");
                }

                if (tvNode != null) ExpectedSequenceTree.Items.Add(tvNode);
            }

            // 2. POPULATE ACTUAL SEQUENCE DIRECTLY FROM LOG FILE (BYPASS C++ ENGINE ENTIRELY)
            DateTime start = Tab2FilterRadio.IsChecked == true ? CombineDateTime(Tab2StartDate.SelectedDate, Tab2StartTime.Text, false) : DateTime.MinValue;
            DateTime end = Tab2FilterRadio.IsChecked == true ? CombineDateTime(Tab2EndDate.SelectedDate, Tab2EndTime.Text, true) : DateTime.MaxValue;

            bool capturingMsg = false;
            string currentMsgType = "";
            int valueCount = 0;
            string tempDataID = "", tempCEID = "", tempS3DataID = "", tempCarrierAction = "", tempCarrierID = "";
            DateTime currentTime = DateTime.MinValue;

            foreach (string line in File.ReadLines(LogPathTextBox.Text))
            {
                if (line.Length >= 19 && DateTime.TryParse(line.Substring(0, 19), out DateTime dt))
                    currentTime = dt;

                if (Tab2FilterRadio.IsChecked == true && currentTime != DateTime.MinValue && (currentTime < start || currentTime > end))
                {
                    capturingMsg = false; continue;
                }

                if (line.Contains("S6F11") && !line.Contains("S6F11_")) { capturingMsg = true; currentMsgType = "S6F11"; valueCount = 0; tempDataID = ""; tempCEID = ""; }
                else if (line.Contains("S3F17") && !line.Contains("S3F17_")) { capturingMsg = true; currentMsgType = "S3F17"; valueCount = 0; tempS3DataID = ""; tempCarrierAction = ""; tempCarrierID = ""; }
                else if (line.Contains("S4F17") && !line.Contains("S4F17_")) { capturingMsg = true; currentMsgType = "S4F17"; valueCount = 0; }
                else if (capturingMsg && (line.Contains("<U") || line.Contains("<I") || line.Contains("<A")))
                {
                    valueCount++;
                    string extractedValue = "";
                    if (line.Contains("'"))
                    {
                        int firstQuote = line.IndexOf('\'');
                        int lastQuote = line.LastIndexOf('\'');
                        if (firstQuote != -1 && lastQuote > firstQuote)
                            extractedValue = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                    }
                    else
                    {
                        string[] parts = line.Split(new char[] { '<', ' ', '>' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2) extractedValue = parts[1].Trim();
                    }

                    TreeViewItem actualNode = null;
                    if (currentMsgType == "S6F11")
                    {
                        if (valueCount == 1) tempDataID = extractedValue;
                        else if (valueCount == 2) tempCEID = extractedValue;
                        else if (valueCount == 3)
                        {
                            actualNode = CreateReadOnlyS6F11Node(new LogEvent { Protocol = "S6F11", DataID = tempDataID, CEID = tempCEID, ReportID = extractedValue, Timestamp = currentTime.ToString("HH:mm:ss") }, "#ABB2BF");
                            capturingMsg = false;
                        }
                    }
                    else if (currentMsgType == "S3F17")
                    {
                        if (valueCount == 1) tempS3DataID = extractedValue;
                        else if (valueCount == 2) tempCarrierAction = extractedValue;
                        else if (valueCount == 3) tempCarrierID = extractedValue;
                        else if (valueCount == 4)
                        {
                            actualNode = CreateReadOnlyS3F17Node(new LogEvent { Protocol = "S3F17", DataID = tempS3DataID, CarrierAction = tempCarrierAction, CarrierID = tempCarrierID, PortID = extractedValue, Timestamp = currentTime.ToString("HH:mm:ss") }, "#ABB2BF");
                            capturingMsg = false;
                        }
                    }
                    else if (currentMsgType == "S4F17")
                    {
                        if (valueCount == 1)
                        {
                            actualNode = CreateReadOnlyS4F17Node(new LogEvent { Protocol = "S4F17", SpoolID = extractedValue, Timestamp = currentTime.ToString("HH:mm:ss") }, "#ABB2BF");
                            capturingMsg = false;
                        }
                    }

                    if (actualNode != null) ActualSequenceTree.Items.Add(actualNode);
                }
            }

            try
            {
                SmartAlignTrees();

                int totalRows = Math.Min(ExpectedSequenceTree.Items.Count, ActualSequenceTree.Items.Count);
                int countPerfect = 0, countMissing = 0, countExtra = 0, countSwaps = 0;
                int totalExpectedTarget = 0;

                var pendingE = new List<(int RowNum, string Key, TreeViewItem Node)>();
                var pendingA = new List<(int RowNum, string Key, TreeViewItem Node)>();

                // PHASE 1: ALIGNMENT & LINE NUMBERING
                for (int k = 0; k < totalRows; k++)
                {
                    int rowNum = k + 1;
                    var eNode = ExpectedSequenceTree.Items[k] as TreeViewItem;
                    var aNode = ActualSequenceTree.Items[k] as TreeViewItem;
                    var eBlock = eNode?.Header as TextBlock;
                    var aBlock = aNode?.Header as TextBlock;

                    // 🌟 Look for the GhostNode Tag
                    bool eIsBlank = eNode?.Tag?.ToString() == "GhostNode";
                    bool aIsBlank = aNode?.Tag?.ToString() == "GhostNode";

                    if (!eIsBlank) totalExpectedTarget++;

                    string eText = eBlock?.Text ?? "";
                    string aText = aBlock?.Text ?? "";
                    string eKey = ExtractPrimaryKey(eText);
                    string aKey = ExtractPrimaryKey(aText);

                    if (eBlock != null && !eBlock.Text.StartsWith("[")) eBlock.Text = $"[{rowNum}] {eBlock.Text}";
                    if (aBlock != null && !aBlock.Text.StartsWith("[")) aBlock.Text = $"[{rowNum}] {aBlock.Text}";

                    if (!eIsBlank && !aIsBlank && eKey == aKey)
                    {
                        eBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x98, 0xC3, 0x79));
                        aBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x98, 0xC3, 0x79));
                        countPerfect++;
                    }
                    else
                    {
                        if (!eIsBlank) pendingE.Add((rowNum, eKey, eNode));
                        if (!aIsBlank) pendingA.Add((rowNum, aKey, aNode));
                    }
                }

                // PHASE 2: SWAPS (Light Purple)
                foreach (var pe in pendingE.ToList())
                {
                    var pa = pendingA.FirstOrDefault(x => x.Key == pe.Key);
                    if (pa.Key != null)
                    {
                        var eBlock = pe.Node.Header as TextBlock;
                        var aBlock = pa.Node.Header as TextBlock;
                        var purpleBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC6, 0x78, 0xDD));

                        eBlock.Foreground = purpleBrush;
                        aBlock.Foreground = purpleBrush;
                        aBlock.Text += $"   <-- (Expected at Line {pe.RowNum})";
                        eBlock.Text += $"   <-- (Swapped to Line {pa.RowNum})";

                        // Color the ghost nodes purple too!
                        if (ActualSequenceTree.Items[pe.RowNum - 1] is TreeViewItem aBlankNode && aBlankNode.Tag?.ToString() == "GhostNode")
                            if (aBlankNode.Header is TextBlock aBlankBlock) aBlankBlock.Foreground = purpleBrush;

                        if (ExpectedSequenceTree.Items[pa.RowNum - 1] is TreeViewItem eBlankNode && eBlankNode.Tag?.ToString() == "GhostNode")
                            if (eBlankNode.Header is TextBlock eBlankBlock) eBlankBlock.Foreground = purpleBrush;

                        countSwaps++;
                        pendingE.Remove(pe);
                        pendingA.Remove(pa);
                    }
                }

                // PHASE 3: MISSING (Light Red) & EXTRAS (Light Yellow)
                foreach (var pe in pendingE)
                {
                    var eBlock = pe.Node.Header as TextBlock;
                    var redBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x6C, 0x75));
                    eBlock.Foreground = redBrush;
                    countMissing++;

                    // Find the Ghost on the Actual side and color it faded Red!
                    if (pe.RowNum - 1 >= 0 && pe.RowNum - 1 < ActualSequenceTree.Items.Count)
                    {
                        if (ActualSequenceTree.Items[pe.RowNum - 1] is TreeViewItem ghostNode && ghostNode.Tag?.ToString() == "GhostNode")
                        {
                            if (ghostNode.Header is TextBlock ghostBlock) ghostBlock.Foreground = redBrush;
                        }
                    }
                }

                foreach (var pa in pendingA)
                {
                    var aBlock = pa.Node.Header as TextBlock;
                    var yellowBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0xC0, 0x7B));
                    aBlock.Foreground = yellowBrush;
                    countExtra++;

                    // Find the Ghost on the Expected side and color it faded Yellow!
                    if (pa.RowNum - 1 >= 0 && pa.RowNum - 1 < ExpectedSequenceTree.Items.Count)
                    {
                        if (ExpectedSequenceTree.Items[pa.RowNum - 1] is TreeViewItem ghostNode && ghostNode.Tag?.ToString() == "GhostNode")
                        {
                            if (ghostNode.Header is TextBlock ghostBlock) ghostBlock.Foreground = yellowBrush;
                        }
                    }
                }

                // PHASE 4: SUMMARY
                if (AnalysisSummaryText != null)
                {
                    if (countPerfect == totalExpectedTarget && countExtra == 0 && countMissing == 0 && countSwaps == 0)
                    {
                        AnalysisSummaryText.Text = $"SEQUENCE VALIDATED: Full Compliance | Matches: {countPerfect}/{totalExpectedTarget}";
                        AnalysisSummaryText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x98, 0xC3, 0x79));
                    }
                    else
                    {
                        string status = (countExtra > 0 || countMissing > 0 || countSwaps > 0) ? "SEQUENCE DRIFT DETECTED" : "SEQUENCE DEVIATION";
                        AnalysisSummaryText.Text = $"{status} | Expected: {totalExpectedTarget} | Matches: {countPerfect} | Swaps: {countSwaps} | Missing: {countMissing} | Extras: {countExtra}";
                        AnalysisSummaryText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x6C, 0x75));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Alignment Error:\n\n{ex.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ExtractPrimaryKey(string headerText)
        {
            if (string.IsNullOrWhiteSpace(headerText)) return "";

            string cleanText = headerText.Contains("]") ? headerText.Split(']')[1].Trim() : headerText.Trim();
            var parts = cleanText.Split('_');

            return parts.Length >= 2 ? $"{parts[0]}_{parts[1]}" : cleanText;
        }

        private void SmartAlignTrees()
        {
            var expNodes = ExpectedSequenceTree.Items.Cast<TreeViewItem>().ToList();
            var actNodes = ActualSequenceTree.Items.Cast<TreeViewItem>().ToList();

            ExpectedSequenceTree.Items.Clear();
            ActualSequenceTree.Items.Clear();

            int[,] dp = new int[expNodes.Count + 1, actNodes.Count + 1];

            for (int i = 1; i <= expNodes.Count; i++)
            {
                for (int j = 1; j <= actNodes.Count; j++)
                {
                    string eText = ((TextBlock)expNodes[i - 1].Header).Text;
                    string aText = ((TextBlock)actNodes[j - 1].Header).Text;

                    string eKey = ExtractPrimaryKey(eText);
                    string aKey = ExtractPrimaryKey(aText);

                    if (!string.IsNullOrEmpty(eKey) && eKey == aKey)
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }

            int x = expNodes.Count, y = actNodes.Count;
            var alignedExp = new Stack<TreeViewItem>();
            var alignedAct = new Stack<TreeViewItem>();

            while (x > 0 || y > 0)
            {
                if (x > 0 && y > 0)
                {
                    string eText = ((TextBlock)expNodes[x - 1].Header).Text;
                    string aText = ((TextBlock)actNodes[y - 1].Header).Text;
                    string eKey = ExtractPrimaryKey(eText);
                    string aKey = ExtractPrimaryKey(aText);

                    if (!string.IsNullOrEmpty(eKey) && eKey == aKey)
                    {
                        alignedExp.Push(expNodes[x - 1]);
                        alignedAct.Push(actNodes[y - 1]);
                        x--; y--;
                        continue;
                    }
                }

                //if (x > 0 && (y == 0 || dp[x - 1, y] >= dp[x, y - 1]))
                //{
                //    alignedExp.Push(expNodes[x - 1]);
                //    alignedAct.Push(CreateBlankNode()); // <-- Back to blanks!
                //    x--;
                //}
                //else
                //{
                //    alignedExp.Push(CreateBlankNode()); // <-- Back to blanks!
                //    alignedAct.Push(actNodes[y - 1]);
                //    y--;
                //}
                // ... inside the while loop in SmartAlignTrees()
                if (x > 0 && (y == 0 || dp[x - 1, y] >= dp[x, y - 1]))
                {
                    alignedExp.Push(expNodes[x - 1]);
                    alignedAct.Push(CreateGhostNode(expNodes[x - 1])); // <-- Use Ghost Node!
                    x--;
                }
                else
                {
                    alignedExp.Push(CreateGhostNode(actNodes[y - 1])); // <-- Use Ghost Node!
                    alignedAct.Push(actNodes[y - 1]);
                    y--;
                }
            }

            while (alignedExp.Count > 0) ExpectedSequenceTree.Items.Add(alignedExp.Pop());
            while (alignedAct.Count > 0) ActualSequenceTree.Items.Add(alignedAct.Pop());
        }

        // ==========================================
        // TAB 3: LOG EXPLORER ENGINE
        // ==========================================
        private void OnSearchLogsClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LogPathTextBox.Text) || !File.Exists(LogPathTextBox.Text)) return;

            string searchType = SearchTypeComboBox.SelectedItem != null ? ((ComboBoxItem)SearchTypeComboBox.SelectedItem).Content.ToString() : "ANY EVENT";
            string searchValue = SearchValueTextBox.Text.Trim();

            bool applyTimeFilter = Tab3FilterRadio.IsChecked == true;
            DateTime filterStart = CombineDateTime(Tab3StartDate.SelectedDate, Tab3StartTime.Text, false);
            DateTime filterEnd = CombineDateTime(Tab3EndDate.SelectedDate, Tab3EndTime.Text, true);

            var allEvents = SdrLogParser.ParseLog(LogPathTextBox.Text, applyTimeFilter, filterStart, filterEnd);
            var filteredEvents = allEvents.AsEnumerable();

            if (!showAllData) filteredEvents = filteredEvents.Where(ev => ev.Protocol == "S6F11" || ev.Protocol == "S3F17");
            //|| ev.Protocol == "S4F17"

            if (!string.IsNullOrEmpty(searchValue))
            {
                if (searchType == "DATA ID") filteredEvents = filteredEvents.Where(ev => ev.DataID == searchValue);
                else if (searchType == "CEID") filteredEvents = filteredEvents.Where(ev => ev.CEID == searchValue || ev.CarrierAction == searchValue );
                //|| ev.SpoolID == searchValue
                else if (searchType == "REPORT ID") filteredEvents = filteredEvents.Where(ev => ev.ReportID == searchValue);
                else filteredEvents = filteredEvents.Where(ev => ev.DataID == searchValue || ev.CEID == searchValue || ev.ReportID == searchValue || ev.CarrierAction == searchValue );
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
                Height = 430,  // <--- Ensure Height is 430 right here!
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 34, 42)),
                ResizeMode = ResizeMode.NoResize
            };

            StackPanel mainPanel = new StackPanel { Margin = new Thickness(20) };

            TextBlock title1 = new TextBlock { Text = "1. UPLOAD SDR LOG", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(97, 175, 239)), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
            Grid fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox txtFile = new TextBox { IsReadOnly = true, Height = 30, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 44, 52)), Foreground = System.Windows.Media.Brushes.White, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 0, 0) };
            Button btnBrowse = new Button { Content = "BROWSE", Width = 80, Margin = new Thickness(10, 0, 0, 0), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 68, 81)), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold };

            Grid.SetColumn(txtFile, 0); Grid.SetColumn(btnBrowse, 1);
            fileGrid.Children.Add(txtFile); fileGrid.Children.Add(btnBrowse);

            TextBlock title2 = new TextBlock { Text = "2. TIME RANGE SELECTION", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(152, 195, 121)), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };

            // --- REBUILD THE TIME GRID PROPERLY ---
            Grid timeGrid = new Grid { Margin = new Thickness(0, 0, 0, 30) };
            timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            timeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            timeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock lblStart = new TextBlock { Text = "START:", Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 10) };
            DatePicker dpStart = new DatePicker { Margin = new Thickness(0, 0, 10, 10), Height = 30 };
            TextBox txtStartTime = new TextBox { Margin = new Thickness(0, 0, 0, 10), Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E222A")), BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E4451")), BorderThickness = new Thickness(1), Foreground = System.Windows.Media.Brushes.White, Padding = new Thickness(5, 0, 0, 0), ToolTip = "HH:mm:ss" };

            TextBlock lblEnd = new TextBlock { Text = "END:", Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            DatePicker dpEnd = new DatePicker { Margin = new Thickness(0, 0, 10, 0), Height = 30 };
            TextBox txtEndTime = new TextBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E222A")), BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E4451")), BorderThickness = new Thickness(1), Foreground = System.Windows.Media.Brushes.White, Padding = new Thickness(5, 0, 0, 0), ToolTip = "HH:mm:ss" };

            // THIS IS WHAT WAS MISSING: Putting everything in the correct Row and Column!
            Grid.SetRow(lblStart, 0); Grid.SetColumn(lblStart, 0);
            Grid.SetRow(dpStart, 0); Grid.SetColumn(dpStart, 1);
            Grid.SetRow(txtStartTime, 0); Grid.SetColumn(txtStartTime, 2);

            Grid.SetRow(lblEnd, 1); Grid.SetColumn(lblEnd, 0);
            Grid.SetRow(dpEnd, 1); Grid.SetColumn(dpEnd, 1);
            Grid.SetRow(txtEndTime, 1); Grid.SetColumn(txtEndTime, 2);

            // Add them to the grid
            timeGrid.Children.Add(lblStart); timeGrid.Children.Add(dpStart); timeGrid.Children.Add(txtStartTime);
            timeGrid.Children.Add(lblEnd); timeGrid.Children.Add(dpEnd); timeGrid.Children.Add(txtEndTime);
            // --- NEW: SEQUENCE NAME INPUT ---
            TextBlock title3 = new TextBlock { Text = "3. SEQUENCE NAME (OPTIONAL)", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 192, 123)), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };
            TextBox txtSequenceName = new TextBox { Height = 30, Margin = new Thickness(0, 0, 0, 30), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E222A")), BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E4451")), BorderThickness = new Thickness(1), Foreground = System.Windows.Media.Brushes.White, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 0, 0), ToolTip = "Leave blank for auto-numbering" };

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
                if (string.IsNullOrWhiteSpace(txtFile.Text)) return;

                DateTime start = CombineDateTime(dpStart.SelectedDate, txtStartTime.Text, false);
                DateTime end = CombineDateTime(dpEnd.SelectedDate, txtEndTime.Text, true);

                var targetEvents = new List<LogEvent>();
                bool capturingMsg = false;
                string currentMsgType = "";
                int valueCount = 0;

                string tempDataID = "", tempCEID = "", tempS3DataID = "", tempCarrierAction = "", tempCarrierID = "";

                DateTime currentTime = DateTime.MinValue;

                foreach (string line in File.ReadLines(txtFile.Text))
                {
                    if (line.Length >= 19 && DateTime.TryParse(line.Substring(0, 19), out DateTime dt))
                    {
                        currentTime = dt;
                    }

                    if (currentTime != DateTime.MinValue && (currentTime < start || currentTime > end))
                    {
                        capturingMsg = false;
                        continue;
                    }

                    if (line.Contains("S6F11") && !line.Contains("S6F11_"))
                    {
                        capturingMsg = true;
                        currentMsgType = "S6F11";
                        valueCount = 0;
                    }
                    else if (line.Contains("S3F17") && !line.Contains("S3F17_"))
                    {
                        capturingMsg = true;
                        currentMsgType = "S3F17";
                        valueCount = 0;
                    }
                    else if (line.Contains("S4F17") && !line.Contains("S4F17_"))
                    {
                        capturingMsg = true;
                        currentMsgType = "S4F17";
                        valueCount = 0;
                    }
                    else if (capturingMsg && (line.Contains("<U") || line.Contains("<I") || line.Contains("<A")))
                    {
                        valueCount++;
                        string extractedValue = "0";

                        if (line.Contains("'"))
                        {
                            int firstQuote = line.IndexOf('\'');
                            int lastQuote = line.LastIndexOf('\'');
                            if (firstQuote != -1 && lastQuote > firstQuote)
                                extractedValue = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                        }
                        else
                        {
                            string[] parts = line.Split(new char[] { '<', ' ', '>' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2) extractedValue = parts[1].Trim();
                        }

                        if (currentMsgType == "S6F11")
                        {
                            if (valueCount == 1) tempDataID = extractedValue;
                            else if (valueCount == 2) tempCEID = extractedValue;
                            else if (valueCount == 3)
                            {
                                targetEvents.Add(new LogEvent { Protocol = "S6F11", DataID = tempDataID, CEID = tempCEID, ReportID = extractedValue });
                                capturingMsg = false;
                            }
                        }
                        else if (currentMsgType == "S3F17")
                        {
                            if (valueCount == 1) tempS3DataID = extractedValue;
                            else if (valueCount == 2) tempCarrierAction = extractedValue;
                            else if (valueCount == 3) tempCarrierID = extractedValue;
                            else if (valueCount == 4)
                            {
                                targetEvents.Add(new LogEvent { Protocol = "S3F17", DataID = tempS3DataID, CarrierAction = tempCarrierAction, CarrierID = tempCarrierID, PortID = extractedValue });
                                capturingMsg = false;
                            }
                        }
                        else if (currentMsgType == "S4F17")
                        {
                            if (valueCount == 1)
                            {
                                targetEvents.Add(new LogEvent { Protocol = "S4F17", SpoolID = extractedValue });
                                capturingMsg = false;
                            }
                        }
                    }
                }

                if (targetEvents.Count == 0)
                {
                    MessageBox.Show("No target events found in this time range.", "Extraction Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                string displayName = $"EXTRACTED SEQUENCE {seqNum}";

                // 🌟 THE FIX: If user typed a custom name, clean it and use it instead!
                if (!string.IsNullOrWhiteSpace(txtSequenceName.Text))
                {
                    string cleanUserInput = txtSequenceName.Text.Trim();
                    displayName = cleanUserInput;

                    // XML tags can't have spaces or certain special characters, so we format it safely
                    string safeXmlName = cleanUserInput.Replace(" ", "_").Replace("-", "_").Replace(".", "_");
                    autoSeqName = $"Sequence_{safeXmlName}";
                }

                XElement newSequenceNode = new XElement(autoSeqName);
                foreach (var parsedEv in targetEvents)
                {
                    XElement msgNode = new XElement(parsedEv.Protocol);
                    XElement outerList = new XElement("List");

                    switch (parsedEv.Protocol)
                    {
                        case "S6F11":
                            string ceidName = EventMapper.GetEventName(parsedEv.CEID);
                            string attrName6 = string.IsNullOrEmpty(ceidName) ? parsedEv.CEID : $"{parsedEv.CEID}_{ceidName}";
                            msgNode.SetAttributeValue("NAME", attrName6);

                            outerList.Add(new XElement("Data_ID", parsedEv.DataID));
                            outerList.Add(new XElement("CEID", parsedEv.CEID));
                            XElement innerList6 = new XElement("List");
                            innerList6.Add(new XElement("Report_ID", parsedEv.ReportID));
                            outerList.Add(innerList6);
                            break;

                        case "S3F17":
                            msgNode.SetAttributeValue("NAME", parsedEv.CarrierAction);
                            outerList.Add(new XElement("DATAID", parsedEv.DataID ?? ""));
                            outerList.Add(new XElement("CARRIERACTION", parsedEv.CarrierAction));
                            outerList.Add(new XElement("CARRIERID", parsedEv.CarrierID));
                            outerList.Add(new XElement("PORTID", parsedEv.PortID));
                            XElement innerList3 = new XElement("List");
                            XElement innerList3_2 = new XElement("List");
                            innerList3_2.Add(new XElement("ATTRID", ""));
                            innerList3_2.Add(new XElement("ATTRDATA", ""));
                            innerList3.Add(innerList3_2);
                            outerList.Add(innerList3);
                            break;

                        case "S4F17":
                            msgNode.SetAttributeValue("NAME", parsedEv.SpoolID);
                            outerList.Add(new XElement("Spool_ID", parsedEv.SpoolID));
                            break;
                    }

                    msgNode.Add(outerList);
                    newSequenceNode.Add(msgNode);
                }

                doc.Root.Add(newSequenceNode);
                doc.Save(filePath);
                InitializeMockDatabase();
                dialog.Close();
                MessageBox.Show($"Extracted {targetEvents.Count} events!\n\nSaved as: EXTRACTED SEQUENCE {seqNum}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            // Assemble dialog UI and show it
            mainPanel.Children.Add(title1);
            mainPanel.Children.Add(fileGrid);
            mainPanel.Children.Add(title2);
            mainPanel.Children.Add(timeGrid);

            // --- ADD NEW ELEMENTS HERE ---
            mainPanel.Children.Add(title3);
            mainPanel.Children.Add(txtSequenceName);

            mainPanel.Children.Add(btnSave);
            dialog.Content = mainPanel;
            dialog.ShowDialog();
        }

        // ==========================================
        // PALETTE & XML MANAGEMENT
        // ==========================================
        private void InitializeSecsPalette()
        {
            TreeView2.Items.Clear();
            var root = new TreeViewItem { Header = "SECS Stream Functions", IsExpanded = true };

            // --- S6F11 ---
            var s6f11 = new TreeViewItem { Header = "S6F11", IsExpanded = false };
            var list1 = new TreeViewItem { Header = "[List]", IsExpanded = true };
            list1.Items.Add(new TreeViewItem { Header = "Data_ID", IsExpanded = true });
            list1.Items.Add(new TreeViewItem { Header = "CEID", IsExpanded = true });
            var list2 = new TreeViewItem { Header = "[List]", IsExpanded = true };
            list2.Items.Add(new TreeViewItem { Header = "Report_ID", IsExpanded = true });
            list1.Items.Add(list2);
            s6f11.Items.Add(list1);
            root.Items.Add(s6f11);

            // --- S3F17 ---
            var s3f17 = new TreeViewItem { Header = "S3F17", IsExpanded = false };
            var list_S3F17 = new TreeViewItem { Header = "[List]", IsExpanded = true };
            list_S3F17.Items.Add(new TreeViewItem { Header = "DATAID", IsExpanded = true });
            list_S3F17.Items.Add(new TreeViewItem { Header = "CARRIERACTION", IsExpanded = true });
            list_S3F17.Items.Add(new TreeViewItem { Header = "CARRIERID", IsExpanded = true });
            list_S3F17.Items.Add(new TreeViewItem { Header = "PORTID", IsExpanded = true });
            var list_2_S3F17 = new TreeViewItem { Header = "[List]", IsExpanded = true };
            var list_3_S3F17 = new TreeViewItem { Header = "[List]", IsExpanded = true };
            list_3_S3F17.Items.Add(new TreeViewItem { Header = "ATTRID", IsExpanded = true });
            list_3_S3F17.Items.Add(new TreeViewItem { Header = "ATTRDATA", IsExpanded = true });
            list_2_S3F17.Items.Add(list_3_S3F17);
            list_S3F17.Items.Add(list_2_S3F17);
            s3f17.Items.Add(list_S3F17);
            root.Items.Add(s3f17);

            // --- S4F17 ---
            var s4f17 = new TreeViewItem { Header = "S4F17", IsExpanded = false };
            var list_S4F17 = new TreeViewItem { Header = "[List]", IsExpanded = true };
            list_S4F17.Items.Add(new TreeViewItem { Header = "Spool_ID", IsExpanded = true });
            s4f17.Items.Add(list_S4F17);
            root.Items.Add(s4f17);

            TreeView2.Items.Add(root);
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

                TreeViewItem funcNode;
                if (selectedPalette.Items.Count > 0) funcNode = CloneTreeViewItem(selectedPalette);
                else funcNode = CreateFunctionNode(cleanName, true);

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
                TreeViewItem funcNode;
                if (selectedPalette.Items.Count > 0) funcNode = CloneTreeViewItem(selectedPalette);
                else funcNode = CreateFunctionNode(cleanName, true);

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
                AddSmartItemToNode(targetNode, cleanName, "");
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

        private TreeViewItem CreateXmlTagRow(string tagName, string value)
        {
            var rowItem = new TreeViewItem { IsExpanded = false, Tag = "TagLeaf" };
            rowItem.ContextMenu = null;

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

            // 🌟 THIS IS THE FIX: Only show brackets if there is actually text inside the value!
            // CHANGE THIS:
            // panel.Children.Add(new TextBlock { Text = $" [{value}]", ... });

            // TO THIS:
            string displayValue = string.IsNullOrWhiteSpace(value) ? "" : $" [{value}]";
            panel.Children.Add(new TextBlock
            {
                Text = displayValue,
                // ... (keep the rest of the styling the same)
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

                var nameBlock = (TextBlock)panel.Children[0];
                var valueBlock = (TextBlock)panel.Children[1];
                string currentName = nameBlock.Text.Trim();

                if (currentName == "CEID")
                {
                    ShowSmartCeidDialog(valueBlock);
                }
                else if (currentName == "CARRIERACTION")
                {
                    ShowSmartCarrierActionDialog(valueBlock);
                }
                else
                {
                    string currentVal = valueBlock.Text.Replace("[", "").Replace("]", "").Trim();
                    var dlg = new ItemDialog(currentName, currentVal) { Owner = this };
                    if (dlg.ShowDialog() == true)
                    {
                        nameBlock.Text = dlg.ItemName.Replace(" ", "_").Replace("(", "_").Replace(")", "_");

                        // 🌟 FIX PART 2: Also hide brackets if the user saves a blank value from the text dialog
                        valueBlock.Text = string.IsNullOrWhiteSpace(dlg.ItemValue) ? "" : $" [{dlg.ItemValue}]";
                    }
                }
            };

            return rowItem;
        }
        private void ShowSmartCeidDialog(TextBlock valueBlock)
        {
            string currentVal = valueBlock.Text.Replace("[", "").Replace("]", "").Trim();

            // Build the dynamic window
            Window dlg = new Window
            {
                Title = "Select CEID",
                Width = 380,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 44, 52)),
                ResizeMode = ResizeMode.NoResize
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(20) };

            // CEID ComboBox
            panel.Children.Add(new TextBlock { Text = "SELECT CEID:", Foreground = System.Windows.Media.Brushes.LightGray, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
            ComboBox cmbCeid = new ComboBox { Height = 30, IsEditable = true, Text = currentVal, Margin = new Thickness(0, 0, 0, 15) };

            // Auto-populate the combo box by reading your EVENTS.txt file
            try
            {
                string eventPath = @"C:\Users\ArJuN\OneDrive\Documents\project phase 1\EVENTS.txt";
                if (File.Exists(eventPath))
                {
                    foreach (var line in File.ReadLines(eventPath))
                    {
                        var parts = line.Split(new[] { '=', ',', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0 && !cmbCeid.Items.Contains(parts[0]))
                        {
                            cmbCeid.Items.Add(parts[0].Trim());
                        }
                    }
                }
            }
            catch { /* Ignore if file missing */ }

            // Event Name Display (Auto-fills)
            panel.Children.Add(new TextBlock { Text = "EVENT NAME:", Foreground = System.Windows.Media.Brushes.LightGray, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
            TextBox txtEventName = new TextBox { Height = 30, IsReadOnly = true, Background = System.Windows.Media.Brushes.DarkGray, Foreground = System.Windows.Media.Brushes.Black, Text = EventMapper.GetEventName(currentVal), Margin = new Thickness(0, 0, 0, 20), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(5, 0, 0, 0) };

            // The Magic: Update Event Name instantly when CEID changes
            cmbCeid.SelectionChanged += (s, e) => {
                if (cmbCeid.SelectedItem != null) txtEventName.Text = EventMapper.GetEventName(cmbCeid.SelectedItem.ToString());
            };
            cmbCeid.KeyUp += (s, e) => {
                txtEventName.Text = EventMapper.GetEventName(cmbCeid.Text);
            };

            // Save Button
            Button btnSave = new Button { Content = "SAVE CEID", Height = 35, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(97, 175, 239)), FontWeight = FontWeights.Bold };
            btnSave.Click += (s, e) =>
            {
                string finalCeid = string.IsNullOrWhiteSpace(cmbCeid.Text) ? "0" : cmbCeid.Text.Trim();
                valueBlock.Text = $" [{finalCeid}]";
                dlg.Close();
            };

            panel.Children.Add(cmbCeid);
            panel.Children.Add(txtEventName);
            panel.Children.Add(btnSave);
            dlg.Content = panel;
            dlg.ShowDialog();
        }
        private void ShowSmartCarrierActionDialog(TextBlock valueBlock)
        {
            string currentVal = valueBlock.Text.Replace("[", "").Replace("]", "").Trim();

            Window dlg = new Window
            {
                Title = "Select Carrier Action",
                Width = 380,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 44, 52)),
                ResizeMode = ResizeMode.NoResize
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(20) };

            panel.Children.Add(new TextBlock { Text = "SELECT CARRIER ACTION:", Foreground = System.Windows.Media.Brushes.LightGray, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });

            ComboBox cmbAction = new ComboBox { Height = 30, IsEditable = true, Margin = new Thickness(0, 0, 0, 25) };

            // Populate with standard values from documentation & logs
            cmbAction.Items.Add("ProceedWithCarrier"); // From log screenshots
            cmbAction.Items.Add("CarrierRelease");     // From log screenshots
            cmbAction.Items.Add("1 (BIND)");
            cmbAction.Items.Add("2 (CANCEL_BIND)");
            cmbAction.Items.Add("3 (PROCEED_WITH_CARRIER)");
            cmbAction.Items.Add("4 (CANCEL_CARRIER_AT_PORT)");
            cmbAction.Items.Add("5 (CANCEL_CARRIER_NOTIFICATION)");

            // Set current text
            cmbAction.Text = currentVal;

            Button btnSave = new Button { Content = "SAVE ACTION", Height = 35, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(198, 120, 221)), FontWeight = FontWeights.Bold };
            btnSave.Click += (s, e) =>
            {
                string finalAction = string.IsNullOrWhiteSpace(cmbAction.Text) ? "0" : cmbAction.Text.Trim();

                // If they picked "1 (BIND)", strip it down to just "1" for the XML, or keep the string if they typed it
                if (finalAction.Contains("(")) finalAction = finalAction.Split(' ')[0];

                valueBlock.Text = $" [{finalAction}]";
                dlg.Close();
            };

            panel.Children.Add(cmbAction);
            panel.Children.Add(btnSave);
            dlg.Content = panel;
            dlg.ShowDialog();
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
                    if (AnalysisSequenceComboBox.Items.Count > 0) AnalysisSequenceComboBox.SelectedIndex = 0;
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

                    if ((headerText.StartsWith("S6F11") || headerText.StartsWith("S3F17") || headerText.StartsWith("S4F17")) && childElement.Attribute("NAME") != null)
                    {
                        string nameVal = childElement.Attribute("NAME").Value;
                        headerText = nameVal.StartsWith(headerText + "_") ? nameVal : $"{headerText}_{nameVal}";
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
            return new ContextMenu { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x28, 0x2C, 0x34)), Foreground = System.Windows.Media.Brushes.White, BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3E, 0x44, 0x51)), BorderThickness = new Thickness(1) };
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
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Answer)) node.Header = $"{baseName}_{dlg.Answer.Replace(" ", "_")}";
            };
            var miEvent = new MenuItem { Header = "Set Event Name", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x98, 0xC3, 0x79)) };
            miEvent.Click += (s, e) => renameAction();
            menu.Items.Add(miEvent);
            node.ContextMenu = menu;
            node.MouseDoubleClick += (s, e) => { if (!node.IsSelected) return; e.Handled = true; renameAction(); };
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
            menu.Items.Add(miItem); menu.Items.Add(miList);
            node.ContextMenu = menu;
            return node;
        }

        private TreeViewItem CloneTreeViewItem(TreeViewItem src)
        {
            if (src == null) return null;

            if (src.Tag?.ToString() == "TagLeaf")
            {
                var panel = src.Header as StackPanel;
                if (panel != null && panel.Children.Count > 1)
                {
                    var nameTb = panel.Children[0] as TextBlock;
                    var valTb = panel.Children[1] as TextBlock;
                    string name = nameTb?.Text ?? "";
                    string val = valTb?.Text.Replace("[", "").Replace("]", "").Trim() ?? "";
                    return CreateXmlTagRow(name, val);
                }
            }

            string headerText = src.Header is TextBlock stb ? stb.Text : src.Header?.ToString() ?? "";
            TreeViewItem dst;

            if (headerText == "[List]") dst = CreateListNode(src.IsExpanded);
            else if (!string.IsNullOrEmpty(headerText) && !headerText.StartsWith("[")) dst = CreateFunctionNode(headerText, src.IsExpanded);
            else dst = new TreeViewItem { Header = headerText, IsExpanded = src.IsExpanded };

            dst.Tag = src.Tag;

            if (src.Header is TextBlock otb && !(dst.Header is TextBlock))
            {
                var ntb = new TextBlock { Text = otb.Text, Foreground = otb.Foreground, FontFamily = otb.FontFamily, FontWeight = otb.FontWeight, VerticalAlignment = otb.VerticalAlignment, Margin = otb.Margin };
                dst.Header = ntb;
            }

            foreach (var obj in src.Items)
            {
                if (obj is TreeViewItem child)
                {
                    if ((child.Items == null || child.Items.Count == 0) && child.Tag?.ToString() != "TagLeaf")
                    {
                        string leafName = child.Header?.ToString() ?? "Item";
                        dst.Items.Add(CreateXmlTagRow(leafName, ""));
                    }
                    else dst.Items.Add(CloneTreeViewItem(child));
                }
                else dst.Items.Add(obj);
            }
            return dst;
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
                    if (node.Name.LocalName.StartsWith("S6F11") || node.Name.LocalName.StartsWith("S3F17") || node.Name.LocalName.StartsWith("S4F17"))
                    {
                        var idNode = node.Descendants("CEID").FirstOrDefault() ?? node.Descendants("CARRIERACTION").FirstOrDefault() ?? node.Descendants("Spool_ID").FirstOrDefault();
                        string idVal = idNode != null ? idNode.Value : "*";

                        string eventName = EventMapper.GetEventName(idVal);
                        string attrName = string.IsNullOrEmpty(eventName) ? idVal : $"{idVal}_{eventName}";

                        string baseMsg = node.Name.LocalName.Substring(0, 5);
                        node.Name = baseMsg;
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
            if (TreeView1.Items.Count == 0) return;
            try
            {
                var rootItem = TreeView1.Items[0] as TreeViewItem;
                string currentSequenceTagName = rootItem.Header.ToString().Replace(" ", "_");
                XElement cleanUpdatedBranch = new XElement(currentSequenceTagName);
                SerializeUiToXmlElement(rootItem, cleanUpdatedBranch);
                MessageBox.Show(new XDocument(cleanUpdatedBranch).ToString(), "Sequence XML Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show($"Failed to generate XML preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
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

        private void OnSaveReportClick(object sender, RoutedEventArgs e)
        {
            if (ExpectedSequenceTree.Items.Count == 0) return;
            SaveFileDialog saveFileDialog = new SaveFileDialog { Filter = "CSV Excel File (*.csv)|*.csv", Title = "Export Analysis Report", FileName = $"Sequence_Report_{DateTime.Now:MMdd_HHmm}.csv" };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName))
                    {
                        writer.WriteLine("LOG ANALYZER 4.0 - SEQUENCE REPORT");
                        writer.WriteLine($"Generated on:,{DateTime.Now}");
                        writer.WriteLine($"Target Sequence:,{((ComboBoxItem)AnalysisSequenceComboBox.SelectedItem)?.Content.ToString().Replace(",", " ")}");
                        writer.WriteLine("");
                        writer.WriteLine("STATUS,EVENT NAME,ID1,ID2,ID3");

                        foreach (TreeViewItem rootNode in ExpectedSequenceTree.Items)
                        {
                            var headerBlock = rootNode.Header as TextBlock;
                            string nodeText = headerBlock?.Text.Replace(",", " ") ?? "Unknown";
                            string colorHex = headerBlock?.Foreground.ToString() ?? "";

                            string status = "FATAL (MISSING)";
                            if (colorHex.Contains("98C379")) status = "MATCHED (PERFECT)";
                            else if (colorHex.Contains("C678DD")) status = "FAILED (OUT OF ORDER)";
                            else if (colorHex.Contains("E5C07B")) status = "WARNING (EXTRA NODE)";

                            string id1 = "-", id2 = "-", id3 = "-";
                            if (rootNode.Items.Count > 0 && rootNode.Items[0] is TreeViewItem outerList)
                            {
                                id1 = ExtractTagValue(outerList.Items[0] as TreeViewItem);
                                if (outerList.Items.Count > 1) id2 = ExtractTagValue(outerList.Items[1] as TreeViewItem);
                                if (outerList.Items.Count > 2) id3 = ExtractTagValue(outerList.Items[2] as TreeViewItem);
                            }

                            writer.WriteLine($"{status},{nodeText},{id1},{id2},{id3}");
                        }
                    }
                    MessageBox.Show("Report exported successfully!\n\nYou can open this file in Excel.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex) { MessageBox.Show($"Error saving report: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
    }
}