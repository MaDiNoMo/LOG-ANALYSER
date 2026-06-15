#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace LOG_EZ
{
	public partial class MainWindow : Window
	{
		private readonly string filePath = @"C:\Users\ArJuN\OneDrive\Documents\sequencesaver\Sequence.xml";
		private bool showAllData = false;

		public MainWindow()
		{
			InitializeComponent();
			if (TreeView1 != null) TreeView1.ContextMenu = null;

			InitializeSecsPalette();
			InitializeMockDatabase();
		}

		// ==========================================
		// DATA STRUCTURE & CONVERTER
		// ==========================================
		public class LogEvent
		{
			public string LogDate { get; set; }
			public string LogTime { get; set; }
			public string Timestamp { get; set; }
			public string SdrMessage { get; set; }
			public string Protocol { get; set; }
			public string WaitBit { get; set; }
			public string DataID { get; set; } = "-";
			public string CEID { get; set; } = "-";
			public string ReportID { get; set; } = "-";
			public int LineNumber { get; set; }

			public string GetSignature() => $"{Protocol} (W:{WaitBit}) [Data:{DataID} | CEID:{CEID} | Rep:{ReportID}]";
		}

		public class HeaderToIconConverter : IValueConverter
		{
			public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => null;
			public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
		}

		// ==========================================
		// UI TOGGLE HANDLERS
		// ==========================================
		private void Tab2TimeToggle_Checked(object sender, RoutedEventArgs e)
		{
			if (Tab2TimeGrid != null && Tab2FilterRadio != null)
				Tab2TimeGrid.Visibility = Tab2FilterRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
		}

		private void Tab3TimeToggle_Checked(object sender, RoutedEventArgs e)
		{
			if (Tab3TimeGrid != null && Tab3FilterRadio != null)
				Tab3TimeGrid.IsEnabled = Tab3FilterRadio.IsChecked == true;
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

		// ==========================================
		// DATE/TIME HELPER 
		// ==========================================
		private DateTime CombineDateTime(DateTime? date, string timeStr, bool isEnd)
		{
			// Default to today if no date is picked
			DateTime baseDate = date ?? DateTime.Today;

			// If they entered a time, attach it to the date
			if (TimeSpan.TryParse(timeStr, out TimeSpan time))
			{
				return baseDate.Add(time);
			}

			// If they left the time blank: 
			// Start time defaults to 00:00:00. End time defaults to 23:59:59
			return isEnd ? baseDate.AddDays(1).AddTicks(-1) : baseDate;
		}

		// ==========================================
		// CORE PARSING ENGINE (Used by Tab 2 & Tab 3)
		// ==========================================
		private List<LogEvent> ParseLog(string path, bool useFilter, DateTime start, DateTime end)
		{
			var list = new List<LogEvent>();
			var regexSF = new Regex(@"(S\d+F\d+)\s*W=([01])");
			var regexApp = new Regex(@"\)\s*(Sdr[a-zA-Z]+)");

			string lastTs = "UNKNOWN", lastApp = "Unknown";
			bool capS6 = false; int intCount = 0;
			string curD = "-", curC = "-", curR = "-";
			int currentLine = 0;

			foreach (string line in File.ReadLines(path))
			{
				currentLine++;
				if (line.Length >= 19 && DateTime.TryParse(line.Substring(0, 19), out _))
				{
					lastTs = line.Substring(0, 19);
					var appMatch = regexApp.Match(line);
					if (appMatch.Success) lastApp = appMatch.Groups[1].Value;
				}

				var sfMatch = regexSF.Match(line);
				if (sfMatch.Success)
				{
					string sf = sfMatch.Groups[1].Value;
					string w = sfMatch.Groups[2].Value;

					if (sf == "S6F11") { capS6 = true; intCount = 0; curD = "-"; curC = "-"; curR = "-"; }
					else
					{
						if (!useFilter || (DateTime.TryParse(lastTs, out DateTime t) && t >= start && t <= end))
							list.Add(new LogEvent
							{
								LogDate = lastTs.Substring(0, 10),
								LogTime = lastTs.Substring(11),
								Timestamp = lastTs,
								SdrMessage = lastApp,
								Protocol = sf,
								WaitBit = w,
								LineNumber = currentLine
							});
						capS6 = false;
					}
				}
				else if (capS6 && (line.Contains("<U") || line.Contains("<I")))
				{
					intCount++;
					var parts = line.Split(new char[] { '<', ' ', '>' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length >= 2)
					{
						if (intCount == 1) curD = parts[1].Trim();
						else if (intCount == 2) curC = parts[1].Trim();
						else if (intCount == 3)
						{
							curR = parts[1].Trim();
							if (!useFilter || (DateTime.TryParse(lastTs, out DateTime t) && t >= start && t <= end))
								list.Add(new LogEvent
								{
									LogDate = lastTs.Substring(0, 10),
									LogTime = lastTs.Substring(11),
									Timestamp = lastTs,
									SdrMessage = lastApp,
									Protocol = "S6F11",
									WaitBit = "1",
									DataID = curD,
									CEID = curC,
									ReportID = curR,
									LineNumber = currentLine
								});
							capS6 = false;
						}
					}
				}
			}
			return list;
		}

		// ==========================================
		// TAB 2: ANALYSIS ENGINE
		// ==========================================
		// ==========================================
		// TAB 2: ANALYSIS ENGINE (WITH SUMMARY)
		// ==========================================
		private void OnAnalyseRunClick(object sender, RoutedEventArgs e)
		{
			if (string.IsNullOrWhiteSpace(LogPathTextBox.Text) || AnalysisSequenceComboBox.SelectedItem == null)
			{
				MessageBox.Show("Please upload a log file and select a sequence first!", "Ready to Run", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}

			ConsoleOutput.Inlines.Clear();
			AppendToConsole($"[SYSTEM] Engine initialized. Reading SDR Log...", "#61AFEF");

			// 1. Get Filters (Using New CombineDateTime Helper)
			bool applyTimeFilter = Tab2FilterRadio.IsChecked == true;
			DateTime start = DateTime.MinValue, end = DateTime.MaxValue;
			if (applyTimeFilter)
			{
				start = CombineDateTime(Tab2StartDate.SelectedDate, Tab2StartTime.Text, false);
				end = CombineDateTime(Tab2EndDate.SelectedDate, Tab2EndTime.Text, true);
				AppendToConsole($"[FILTER] Time restricted from {start} to {end}", "#C678DD");
			}

			// 2. Load Expected Sequence
			string seqTag = ((ComboBoxItem)AnalysisSequenceComboBox.SelectedItem).Tag.ToString();
			XDocument seqDoc = XDocument.Load(filePath);
			var expectedEvents = new List<LogEvent>();

			foreach (var s6f11Node in seqDoc.Root?.Element(seqTag)?.Elements("S6F11") ?? Enumerable.Empty<XElement>())
			{
				expectedEvents.Add(new LogEvent
				{
					DataID = s6f11Node.Descendants("Data_ID").FirstOrDefault()?.Value ?? "*",
					CEID = s6f11Node.Descendants("CEID").FirstOrDefault()?.Value ?? "*",
					ReportID = s6f11Node.Descendants("Report_ID").FirstOrDefault()?.Value ?? "*"
				});
			}

			if (expectedEvents.Count == 0) { AppendToConsole("[ERROR] Sequence contains no S6F11 data!", "#E06C75"); return; }

			// 3. Parse Log
			var actualEvents = ParseLog(LogPathTextBox.Text, applyTimeFilter, start, end);

			// 4. Verification Loop
			int highestIndexReached = -1;
			int perfectMatches = 0;

			// --- NEW: Summary Tracking Lists ---
			var matchedList = new List<string>();
			var outOfOrderList = new List<string>();
			var missingList = new List<string>();
            AppendToConsole($"\n==========================================================================================", "#EAC41C");
            AppendToConsole($" STATUS    MESSAGE            DATE      TIME      W     DATA ID      CEID     REPORT ID                 ", "#EAC41C");
            AppendToConsole($"==========================================================================================", "#EAC41C");


            for (int i = 0; i < expectedEvents.Count; i++)

			{
                
                var target = expectedEvents[i];

				bool IsMatch(LogEvent ev) =>
					ev.Protocol == "S6F11" &&
					(target.DataID == "*" || target.DataID == ev.DataID) &&
					(target.CEID == "*" || target.CEID == ev.CEID) &&
					(target.ReportID == "*" || target.ReportID == ev.ReportID);

				int foundIndex = actualEvents.FindIndex(Math.Max(0, highestIndexReached), IsMatch);

				if (foundIndex != -1)
				{
					var ev = actualEvents[foundIndex];
					string prefix = i == 0 ? "[START]" : "[MATCH]";
					AppendToConsole($"{prefix,-9} {ev.SdrMessage,-15} [{ev.Timestamp}] {target.GetSignature()}", "#98C379");

					matchedList.Add(target.GetSignature()); // Track for summary
					highestIndexReached = foundIndex;
					perfectMatches++;
				}
				else
				{
					int previousIndex = actualEvents.FindIndex(0, IsMatch);
					if (previousIndex != -1 && previousIndex < highestIndexReached)
					{
						var ev = actualEvents[previousIndex];
						AppendToConsole($"{"[WARN]",-9} {ev.SdrMessage,-15} [{ev.Timestamp}] {target.GetSignature()} (OUT OF ORDER)", "#E5C07B");
						outOfOrderList.Add(target.GetSignature()); // Track for summary
					}
					else
					{
						AppendToConsole($"{"[FATAL]",-9} {"[-- MISSING --]",-15} {"[-- TIME UNKNOWN --]",-21} {target.GetSignature()}", "#E06C75");
						missingList.Add(target.GetSignature()); // Track for summary
					}
				}
			}

			// --- NEW: Print Final Summary Block ---
			AppendToConsole($"\n=======================================================", "#EAC41C");
			AppendToConsole($"                 FINAL EVENT SUMMARY                   ", "#EAC41C");
			AppendToConsole($"=======================================================", "#EAC41C");

			AppendToConsole($"\n[SUCCESSFUL EVENTS] ({matchedList.Count}):", "#98C379");
			if (matchedList.Count == 0) AppendToConsole("  None", "#ABB2BF");
			else foreach (var f in matchedList) AppendToConsole($"  + {f}", "#98C379");// f instead of e

			if (outOfOrderList.Count > 0)
			{
				AppendToConsole($"\n[OUT OF ORDER EVENTS] ({outOfOrderList.Count}):", "#E5C07B");// f instead of e
				foreach (var f in outOfOrderList) AppendToConsole($"  ~ {f}", "#E5C07B");
			}

			AppendToConsole($"\n[MISSING EVENTS] ({missingList.Count}):", "#E06C75");
			if (missingList.Count == 0) AppendToConsole("  None", "#ABB2BF");
			else foreach (var f in missingList) AppendToConsole($"  - {f}", "#E06C75");// f instead of e

			AppendToConsole($"\n=======================================================", "#ABB2BF");
			AppendToConsole($"[ANALYSIS FINISHED] Perfect Signature Matches: {perfectMatches}/{expectedEvents.Count}\n", "#61AFEF");
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

			// 1. Get Filters
			string searchType = SearchTypeComboBox.SelectedItem != null ? ((ComboBoxItem)SearchTypeComboBox.SelectedItem).Content.ToString() : "ANY EVENT";
			string searchValue = SearchValueTextBox.Text.Trim();

			// Use the New CombineDateTime Helper
			bool applyTimeFilter = Tab3FilterRadio.IsChecked == true;
			DateTime filterStart = DateTime.MinValue, filterEnd = DateTime.MaxValue;
			if (applyTimeFilter)
			{
				filterStart = CombineDateTime(Tab3StartDate.SelectedDate, Tab3StartTime.Text, false);
				filterEnd = CombineDateTime(Tab3EndDate.SelectedDate, Tab3EndTime.Text, true);
			}

			// 2. Parse Log
			var allEvents = ParseLog(LogPathTextBox.Text, applyTimeFilter, filterStart, filterEnd);
			var filteredEvents = allEvents.AsEnumerable();

			// 3. Apply View Mode (S6F11 only vs All Data)
			if (!showAllData)
			{
				filteredEvents = filteredEvents.Where(ev => ev.Protocol == "S6F11");
			}

			// 4. Apply Text Search
			if (!string.IsNullOrEmpty(searchValue))
			{
				if (searchType == "DATA ID") filteredEvents = filteredEvents.Where(ev => ev.DataID == searchValue);
				else if (searchType == "CEID") filteredEvents = filteredEvents.Where(ev => ev.CEID == searchValue);
				else if (searchType == "REPORT ID") filteredEvents = filteredEvents.Where(ev => ev.ReportID == searchValue);
				else filteredEvents = filteredEvents.Where(ev => ev.DataID == searchValue || ev.CEID == searchValue || ev.ReportID == searchValue);
			}

			// 5. Update UI
			var finalResults = filteredEvents.ToList();
			SearchResultsGrid.ItemsSource = finalResults;
			ExploreStatsText.Text = $"Found: {finalResults.Count} matching events";
		}


		// ==========================================
		// AUTO-EXTRACT SEQUENCE FROM LOG FILE
		// ==========================================
		private void BtnAutoExtract_Click(object sender, RoutedEventArgs e)
		{
			OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*", Title = "Select 'Golden' SDR Log" };

			if (openFileDialog.ShowDialog() == true)
			{
				try
				{
					var extractedEvents = ParseLog(openFileDialog.FileName, false, DateTime.MinValue, DateTime.MaxValue)
										  .Where(x => x.Protocol == "S6F11").ToList();

					if (extractedEvents.Count == 0) { MessageBox.Show("No S6F11 triplets found."); return; }

					string autoSeqName = $"Sequence_Auto_{DateTime.Now:MMdd_HHmm}";
					XElement newSequenceNode = new XElement(autoSeqName);

					foreach (var ev in extractedEvents)
					{
						XElement s6f11Node = new XElement("S6F11");
						XElement outerList = new XElement("List");
						outerList.Add(new XElement("Data_ID", ev.DataID));
						outerList.Add(new XElement("CEID", ev.CEID));
						XElement innerList = new XElement("List");
						innerList.Add(new XElement("Report_ID", ev.ReportID));
						outerList.Add(innerList);
						s6f11Node.Add(outerList);
						newSequenceNode.Add(s6f11Node);
					}

					XDocument doc;
					if (File.Exists(filePath))
					{
						try { doc = XDocument.Load(filePath); } catch { doc = new XDocument(new XElement("SequenceRoot")); }
					}
					else doc = new XDocument(new XElement("SequenceRoot"));

					if (doc.Root == null) doc.Add(new XElement("SequenceRoot"));
					doc.Root.Add(newSequenceNode);
					doc.Save(filePath);
					InitializeMockDatabase();

					MessageBox.Show($"Extracted {extractedEvents.Count} complete S6F11 events!\nSaved as: {autoSeqName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
				}
				catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}"); }
			}
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



		//private void BtnShow_Crop(object sender, RoutedEventArgs e)
		//{
		//    MessageBox.Show("Take Rest Brother :D ");
		//    MessageBox.Show("Come on ..dont try to fool me >.<");
		//    MessageBox.Show("STOP WORKING !!!!!!!!!! ");
		//    MessageBox.Show("Finee you can do wht ever you want :( and Dont complain about the backpain ");


		//}



		private void LoadXmlElementsRecursively(XElement currentXmlElement, TreeViewItem visualParentItem)
		{
			foreach (XElement childElement in currentXmlElement.Elements())
			{
				bool isList = childElement.Name.LocalName == "List" || childElement.Name.LocalName == "[List]";

				if (isList || childElement.HasElements)
				{
					var folderNode = isList ? CreateListNode(false) : CreateFunctionNode(childElement.Name.LocalName, false);
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

		private void OnUploadLogClick(object sender, RoutedEventArgs e)
		{
			OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*";
			openFileDialog.Title = "Select SDR Log File";

			if (openFileDialog.ShowDialog() == true)
			{
				LogPathTextBox.Text = openFileDialog.FileName;
				AppendToConsole($"[FILE LOADED] Loaded SDR Log: {Path.GetFileName(openFileDialog.FileName)}", "#E5C07B");
			}
		}

		private void OnSaveReportClick(object sender, RoutedEventArgs e)
		{
			MessageBox.Show("This will export the analysis results to a CSV/PDF in a future update.", "Export Module", MessageBoxButton.OK, MessageBoxImage.Information);
		}

		private void AppendToConsole(string text, string hexColor)
		{
			var run = new System.Windows.Documents.Run(text + "\n")
			{
				Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor))
			};
			ConsoleOutput.Inlines.Add(run);
		}
	}
}