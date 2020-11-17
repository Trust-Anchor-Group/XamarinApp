using System;
using System.ComponentModel;
using Waher.Events;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Markup;
using ZXing;

namespace XamarinApp.MainMenu
{
	// Learn more about making custom code visible in the Xamarin.Forms previewer
	// by visiting https://aka.ms/xamarinforms-previewer
	[DesignTimeVisible(true)]
	public partial class ScanQrCodePage : ContentPage, IBackButton
	{
		private readonly Page owner;
		private readonly bool modal;
		private string result = string.Empty;

		public ScanQrCodePage(Page Owner, bool Modal)
		{
			this.owner = Owner;
			this.modal = Modal;
			this.BindingContext = this;
			InitializeComponent();
		}

		private void BackButton_Clicked(object sender, EventArgs e)
		{
			if (this.modal)
				this.Navigation.PopModalAsync();
			else
				App.ShowPage(this.owner, true);
		}

		private void ModeButton_Clicked(object sender, EventArgs e)
		{
			bool Manual = !this.ManualGrid.IsVisible;

			this.ScanGrid.IsVisible = !Manual;
			this.ManualGrid.IsVisible = Manual;

			this.ModeButton.Text = Manual ? "Scan Code" : "Enter Manually";

			if (Manual)
				this.Link.Focus();
		}

		public void Scanner_OnScanResult(Result result)
		{
			this.result = result.Text;

			if (!(string.IsNullOrEmpty(result?.Text)))
			{
				if (this.modal)
				{
					try
					{
						CodeScanned?.Invoke(this, new EventArgs());
					}
					catch (Exception ex)
					{
						Log.Critical(ex);
					}

					this.BackClicked();
				}
				else
				{
					Device.BeginInvokeOnMainThread(() =>
					{
						this.Link.Text = result.Text;
						this.ScanGrid.IsVisible = false;
						this.ManualGrid.IsVisible = true;
						this.ModeButton.Text = "Scan Code";
						this.OpenButton.Focus();
					});
				}
			}
		}

		public event EventHandler CodeScanned = null;
		public string Result => this.result;

		private async void OpenButton_Clicked(object sender, EventArgs e)
		{
			try
			{
				string Code = this.Link.Text;
				Uri Uri = new Uri(Code);

				switch (Uri.Scheme.ToLower())
				{
					case "iotid":
						string LegalId = Code.Substring(6);
						App.ShowPage(this.owner, true);
						await App.OpenLegalIdentity(LegalId, "Scanned QR Code.");
						break;

					case "iotsc":
						string ContractId = Code.Substring(6);
						App.ShowPage(this.owner, true);
						await App.OpenContract(ContractId, "Scanned QR Code.");
						break;

					case "iotdisco":
						// TODO
						break;

					default:
						if (!await Launcher.TryOpenAsync(Uri))
							App.DisplayMessage("Error", "Code not understood:\r\n\r\n" + Code);
						break;
				}
			}
			catch (Exception ex)
			{
				App.DisplayMessage(ex);
			}
		}

		public bool BackClicked()
		{
			this.BackButton_Clicked(this, new EventArgs());
			return true;
		}

	}
}
