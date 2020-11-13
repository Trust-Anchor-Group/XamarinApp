using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Xamarin.Forms;
using Waher.IoTGateway.Setup;
using Waher.Networking.XMPP.Contracts;
using Waher.Persistence;
using Waher.Runtime.Temporary;
using System.Runtime.CompilerServices;

namespace XamarinApp.MainMenu
{
	// Learn more about making custom code visible in the Xamarin.Forms previewer
	// by visiting https://aka.ms/xamarinforms-previewer
	[DesignTimeVisible(true)]
	public partial class IdentityPage : ContentPage, INotifyPropertyChanged, ILegalIdentityChanged, IBackButton
	{
		private readonly SignaturePetitionEventArgs review;
		private readonly XmppConfiguration xmppConfiguration;
		private readonly Page owner;
		private readonly bool personal;
		private LegalIdentity identity;

		public IdentityPage(XmppConfiguration XmppConfiguration, Page Owner)
			: this(XmppConfiguration, Owner, XmppConfiguration.LegalIdentity, true, null)
		{
		}

		public IdentityPage(XmppConfiguration XmppConfiguration, Page Owner, LegalIdentity Identity)
			: this(XmppConfiguration, Owner, Identity, XmppConfiguration.LegalIdentity.Id == Identity.Id, null)
		{
		}

		public IdentityPage(XmppConfiguration XmppConfiguration, Page Owner, LegalIdentity Identity, SignaturePetitionEventArgs Review)
			: this(XmppConfiguration, Owner, Identity, XmppConfiguration.LegalIdentity.Id == Identity.Id, Review)
		{
		}

		private IdentityPage(XmppConfiguration XmppConfiguration, Page Owner, LegalIdentity Identity, bool Personal, SignaturePetitionEventArgs Review)
		{
			this.xmppConfiguration = XmppConfiguration;
			this.owner = Owner;
			this.identity = Identity;
			this.personal = Personal;
			this.review = Review;

			this.BindingContext = this;
			InitializeComponent();

			byte[] Png = QR.GenerateCodePng("iotid:" + Identity.Id, (int)this.QrCode.WidthRequest, (int)this.QrCode.HeightRequest);
			this.QrCode.Source = ImageSource.FromStream(() => new MemoryStream(Png));
			this.QrCode.IsVisible = true;

			if (!Personal)
				this.IdentitySection.Remove(this.NetworkView);

			if (this.review is null)
			{
				this.ButtonSection.Remove(this.CarefulReviewCell);
				this.ButtonSection.Remove(this.ApprovePiiCell);
				this.ButtonSection.Remove(this.PinCell);
				this.ButtonSection.Remove(this.ApproveReviewCell);
				this.ButtonSection.Remove(this.RejectReviewCell);
			}

			this.LoadPhotos();
		}

		public bool ForReview => !(this.review is null);
		public bool NotForReview => (this.review is null);
		public bool IsPersonal => this.personal;
		public bool NotPersonal => !this.personal;
		public bool ForReviewAndPin => !(this.review is null) && xmppConfiguration.UsePin;

		private async void LoadPhotos()
		{
			if (!(this.identity.Attachments is null))
			{
				int i = this.TableView.Root.IndexOf(this.ButtonSection);
				TableSection PhotoSection = new TableSection();
				this.TableView.Root.Insert(i++, PhotoSection);

				foreach (Attachment Attachment in this.identity.Attachments)
				{
					if (Attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
					{
						ViewCell ViewCell;

						try
						{
							KeyValuePair<string, TemporaryFile> P = await App.Contracts.GetAttachmentAsync(Attachment.Url, 10000);

							using (TemporaryFile File = P.Value)
							{
								MemoryStream ms = new MemoryStream();

								File.Position = 0;
								await File.CopyToAsync(ms);
								ms.Position = 0;

								ViewCell = new ViewCell()
								{
									View = new Image()
									{
										Source = ImageSource.FromStream(() => ms)
									}
								};
							}
						}
						catch (Exception ex)
						{
							ViewCell = new ViewCell()
							{
								View = new Label()
								{
									Text = ex.Message
								}
							};
						}

						await Device.InvokeOnMainThreadAsync(() =>
						{
							PhotoSection.Add(ViewCell);
						});
					}
				}
			}
		}

		private void BackButton_Clicked(object sender, EventArgs e)
		{
			App.ShowPage(this.owner, true);
		}

		public DateTime Created => this.identity.Created;
		public DateTime? Updated => CheckMin(this.identity.Updated);
		public string LegalId => this.identity.Id;
		public string BareJid => this.personal ? App.Xmpp?.BareJID ?? string.Empty : string.Empty;
		public string State => this.identity.State.ToString();
		public DateTime? From => CheckMin(this.identity.From);
		public DateTime? To => CheckMin(this.identity.To);
		public string FirstName => this.identity["FIRST"];
		public string MiddleNames => this.identity["MIDDLE"];
		public string LastNames => this.identity["LAST"];
		public string PNr => this.identity["PNR"];
		public string Address => this.identity["ADDR"];
		public string Address2 => this.identity["ADDR2"];
		public string PostalCode => this.identity["ZIP"];
		public string Area => this.identity["AREA"];
		public string City => this.identity["CITY"];
		public string Region => this.identity["REGION"];
		public string Country => this.identity["COUNTRY"];
		public bool IsApproved => this.identity.State == IdentityState.Approved;

		private static DateTime? CheckMin(DateTime? TP)
		{
			if (!TP.HasValue || TP.Value == DateTime.MinValue)
				return null;
			else
				return TP;
		}

		public void LegalIdentityChanged(LegalIdentity Identity)
		{
			this.OnPropertyChanged("Created");
			this.OnPropertyChanged("Updated");
			this.OnPropertyChanged("LegalId");
			this.OnPropertyChanged("BareJid");
			this.OnPropertyChanged("State");
			this.OnPropertyChanged("From");
			this.OnPropertyChanged("To");
			this.OnPropertyChanged("FirstName");
			this.OnPropertyChanged("MiddleNames");
			this.OnPropertyChanged("LastNames");
			this.OnPropertyChanged("PNr");
			this.OnPropertyChanged("Address");
			this.OnPropertyChanged("Address2");
			this.OnPropertyChanged("PostalCode");
			this.OnPropertyChanged("Area");
			this.OnPropertyChanged("City");
			this.OnPropertyChanged("Region");
			this.OnPropertyChanged("Country");
			this.OnPropertyChanged("IsApproved");
		}

		private async void RevokeButton_Clicked(object sender, EventArgs e)
		{
			if (!this.personal)
				return;

			try
			{
				if (!await this.DisplayAlert("Confirm", "Are you sure you want to revoke your legal identity from the application?", "Yes", "Cancel"))
					return;

				LegalIdentity Identity = await App.Contracts.ObsoleteLegalIdentityAsync(this.identity.Id);

				this.identity = Identity;
				this.xmppConfiguration.Step = 2;

				await Database.Update(this.xmppConfiguration);

				this.BackClicked();
			}
			catch (Exception ex)
			{
				await this.DisplayAlert("Error", ex.Message, "OK");
			}
		}

		private async void CompromizedButton_Clicked(object sender, EventArgs e)
		{
			if (!this.personal)
				return;

			try
			{
				if (!await this.DisplayAlert("Confirm", "Are you sure you want to report your legal identity as compromized, stolen or hacked?", "Yes", "Cancel"))
					return;

				LegalIdentity Identity = await App.Contracts.CompromisedLegalIdentityAsync(this.identity.Id);
				
				this.identity = Identity;
				this.xmppConfiguration.Step = 2;

				await Database.Update(this.xmppConfiguration);

				this.BackClicked();
			}
			catch (Exception ex)
			{
				await this.DisplayAlert("Error", ex.Message, "OK");
			}
		}

		public bool BackClicked()
		{
			this.BackButton_Clicked(this, new EventArgs());
			return true;
		}

		private async void ApproveReviewButton_Clicked(object sender, EventArgs e)
		{
			if (this.review is null)
				return;

			try
			{
				if ((!string.IsNullOrEmpty(this.FirstName) && !this.FirstNameCheck.IsChecked) ||
					(!string.IsNullOrEmpty(this.MiddleNames) && !this.MiddleNameCheck.IsChecked) ||
					(!string.IsNullOrEmpty(this.LastNames) && !this.LastNameCheck.IsChecked) ||
					(!string.IsNullOrEmpty(this.PNr) && !this.PersonalNumberCheck.IsChecked) ||
					(!string.IsNullOrEmpty(this.Address) && !this.AddressCheck.IsChecked) ||
					(!string.IsNullOrEmpty(this.Address2) && !this.Address2Check.IsChecked) ||
					(!string.IsNullOrEmpty(this.PostalCode) && !this.PostalCodeCheck.IsChecked) ||
					(!string.IsNullOrEmpty(this.Area) && !this.AreaCheck.IsChecked) ||
					(!string.IsNullOrEmpty(this.City) && !this.CityCheck.IsChecked) ||
					(!string.IsNullOrEmpty(this.Region) && !this.RegionCheck.IsChecked) ||
					(!string.IsNullOrEmpty(this.Country) && !this.CountryCheck.IsChecked))
				{
					await this.DisplayAlert("Incomplete", "Please review all information above, and check the corresponding check boxes if the information is correct. This must be done before you can approve the information.", "OK");
					return;
				}

				if (!this.CarefulReviewCheck.IsChecked)
				{
					await this.DisplayAlert("Incomplete", "You need to check the box you have carefully reviewed all corresponding information above.", "OK");
					return;
				}

				if (!this.ApprovePiiCheck.IsChecked)
				{
					await this.DisplayAlert("Incomplete", "You need to approve to associate your personal information with the identity you review. When third parties review the information in the identity, they will have access to the identity of the reviewers, for transparency.", "OK");
					return;
				}

				if (this.xmppConfiguration.UsePin && this.xmppConfiguration.ComputePinHash(this.PIN.Text) != this.xmppConfiguration.PinHash)
				{
					await this.DisplayAlert("Error", "Invalid PIN.", "OK");
					return;
				}

				byte[] Signature = await App.Contracts.SignAsync(this.review.ContentToSign);

				await App.Contracts.PetitionSignatureResponseAsync(this.review.SignatoryIdentityId, this.review.ContentToSign, 
					Signature, this.review.PetitionId, this.review.RequestorFullJid, true);

				this.BackClicked();
			}
			catch (Exception ex)
			{
				await this.DisplayAlert("Error", ex.Message, "OK");
			}
		}

		private async void RejectReviewButton_Clicked(object sender, EventArgs e)
		{
			if (this.review is null)
				return;

			try
			{
				await App.Contracts.PetitionSignatureResponseAsync(this.review.SignatoryIdentityId,
					this.review.ContentToSign, new byte[0], this.review.PetitionId, this.review.RequestorFullJid, false);

				this.BackClicked();
			}
			catch (Exception ex)
			{
				await this.DisplayAlert("Error", ex.Message, "OK");
			}
		}

	}
}
