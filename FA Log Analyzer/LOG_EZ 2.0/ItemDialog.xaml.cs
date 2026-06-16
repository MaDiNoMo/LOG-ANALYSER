using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;



namespace LOG_EZ
{
    public partial class ItemDialog : Window
    {
        
        public string ItemName { get; private set; } = string.Empty;
        public string ItemValue { get; private set; } = string.Empty;

        public ItemDialog(string defaultName, string defaultValue)
        {
            InitializeComponent();
            NameBox.Text = defaultName;
            ValueBox.Text = defaultValue;

            // Auto-focus the value box so you can start typing immediately
            ValueBox.Focus();
            ValueBox.SelectAll();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ItemName = NameBox.Text.Trim();
            ItemValue = ValueBox.Text.Trim();
            DialogResult = true; // Closes window and signals success
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}