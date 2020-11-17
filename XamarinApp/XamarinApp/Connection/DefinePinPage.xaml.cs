using System;
using System.ComponentModel;
using Xamarin.Forms;
using Waher.IoTGateway.Setup;
using Waher.Persistence;

namespace XamarinApp.Connection
{
    // Learn more about making custom code visible in the Xamarin.Forms previewer
    // by visiting https://aka.ms/xamarinforms-previewer
    [DesignTimeVisible(true)]
    public partial class DefinePinPage : ContentPage, IBackButton
    {
        private readonly XmppConfiguration xmppConfiguration;

        public DefinePinPage(XmppConfiguration XmppConfiguration)
        {
            this.xmppConfiguration = XmppConfiguration;
            InitializeComponent();
            this.BindingContext = this;
        }

        private async void BackButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (this.xmppConfiguration.Step > 0)
                {
                    this.xmppConfiguration.Step--;
                    await Database.Update(this.xmppConfiguration);
                }

                await App.ShowPage();
            }
            catch (Exception ex)
            {
                App.DisplayMessage(ex);
            }
        }

        private void Pin_Completed(object sender, EventArgs e)
        {
            this.RetypePin.Focus();
        }

        private void RetypePin_Completed(object sender, EventArgs e)
        {
            this.ScrollView.ScrollToAsync(this.BackButton, ScrollToPosition.End, false);
        }

        private async void OkButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (this.Pin.Text.Length < 8)
                {
                    this.Pin.Focus();
                    App.DisplayMessage("Error", "PIN number too short. At least 8 numbers (or characters) are required.");
                }
                else if (this.Pin.Text.Trim() != this.Pin.Text)
                {
                    this.Pin.Focus();
                    App.DisplayMessage("Error", "PIN number must not unclude leading or trailing white-space.");
                }
                else if (this.Pin.Text != this.RetypePin.Text)
                {
                    this.Pin.Focus();
                    App.DisplayMessage("Error", "PIN numbers (or passwords) do not match.");
                }
                else
                {
                    this.xmppConfiguration.Pin = this.Pin.Text;
                    this.xmppConfiguration.UsePin = true;

                    if (this.xmppConfiguration.Step == 4)
                        this.xmppConfiguration.Step++;

                    await Database.Update(this.xmppConfiguration);

                    await App.ShowPage();
                }
            }
            catch (Exception ex)
            {
                App.DisplayMessage(ex);
            }
        }

        private async void SkipButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                this.xmppConfiguration.Pin = string.Empty;
                this.xmppConfiguration.UsePin = false;

                if (this.xmppConfiguration.Step == 4)
                    this.xmppConfiguration.Step++;

                await Database.Update(this.xmppConfiguration);

                await App.ShowPage();
            }
            catch (Exception ex)
            {
                App.DisplayMessage(ex);
            }
        }

        public bool IsPinOk
        {
            get
            {
                string Pin = this.Pin?.Text ?? string.Empty;
                return Pin.Length >= 8 && Pin == this.RetypePin.Text && Pin.Trim() == Pin;
            }
        }

        private void Pin_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.OnPropertyChanged("IsPinOk");
        }

        public bool BackClicked()
        {
            this.BackButton_Clicked(this, new EventArgs());
            return true;
        }

    }
}
