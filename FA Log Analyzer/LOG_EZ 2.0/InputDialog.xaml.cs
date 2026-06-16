using System.Windows;

namespace LOG_EZ
{
    public partial class InputDialog : Window
    {
        public string Answer => txtInput.Text;

        public InputDialog(string prompt, string defaultAnswer = "")
        {
            InitializeComponent();
            lblPrompt.Text = prompt;
            txtInput.Text = defaultAnswer;
            txtInput.Focus();
            txtInput.SelectAll();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}