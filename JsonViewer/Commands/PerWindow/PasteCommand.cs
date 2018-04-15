namespace JsonViewer.Commands.PerWindow
{
    using System;
    using System.Diagnostics;
    using JsonViewer.Model;
    using JsonViewer.View;

    public class PasteCommand : BaseCommand
    {
        public PasteCommand(TabContent tab)
            : base("Paste")
        {
            this.ForceVisibility = System.Windows.Visibility.Visible;

            this.Tab = tab;
            this.Tab.Raw_TextBox.TextChanged += OnRawTextBoxChanged;
            this.Tab.ClipboardManager.ClipboardChanged += OnClipboardChanged;
            this.Update();
        }

        public override void Execute(object parameter)
        {
            string jsonString = ClipboardManager.TryGetText();
            Debug.Assert(JsonObjectFactory.TryAgressiveDeserialize(jsonString).Result.IsSuccessful());
            this.Tab.Raw_TextBox.Text = jsonString;
            this.Tab.SetDisplayMode(TabContent.DisplayMode.TreeView);
            this.Update();
        }

        private async void Update()
        {
            string jsonString = ClipboardManager.TryGetText();
            bool possible = !string.IsNullOrWhiteSpace(jsonString) && this.Tab.Raw_TextBox.Text != jsonString;
            if (possible)
            {
                DeserializeResult deserializeResult = await JsonObjectFactory.TryAgressiveDeserialize(jsonString);
                this.SetCanExecute(deserializeResult.IsSuccessful());
            }
            else
            {
                this.SetCanExecute(false);
            }
        }

        private void OnRawTextBoxChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            this.Update();
        }

        private void OnClipboardChanged(object sender, EventArgs e)
        {
            this.Update();
        }
    }
}
