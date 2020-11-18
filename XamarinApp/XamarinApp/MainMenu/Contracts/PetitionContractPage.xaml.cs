﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Xamarin.Forms;
using Waher.IoTGateway.Setup;
using Waher.Networking.XMPP.Contracts;
using Waher.Runtime.Temporary;

namespace XamarinApp.MainMenu.Contracts
{
	// Learn more about making custom code visible in the Xamarin.Forms previewer
	// by visiting https://aka.ms/xamarinforms-previewer
	[DesignTimeVisible(true)]
	public partial class PetitionContractPage : ContentPage, IBackButton
	{
		private readonly Page owner;
		private readonly LegalIdentity requestorIdentity;
		private readonly Contract requestedContract;
		private readonly string requestorFullJid;
		private readonly string petitionId;
		private readonly string purpose;

		public PetitionContractPage(XmppConfiguration XmppConfiguration, Page Owner, LegalIdentity RequestorIdentity, string RequestorFullJid,
			Contract RequestedContract, string PetitionId, string Purpose)
		{
			this.owner = Owner;
			this.requestorIdentity = RequestorIdentity;
			this.requestorFullJid = RequestorFullJid;
			this.requestedContract = RequestedContract;
			this.petitionId = PetitionId;
			this.purpose = Purpose;
			this.BindingContext = this;
			InitializeComponent();

			ViewContractPage Info = new ViewContractPage(XmppConfiguration, Owner, RequestedContract, true);
			Info.MoveInfo(this.TableView);

			this.LoadPhotos();
		}

		private async void LoadPhotos()
		{
			if (!(this.requestorIdentity.Attachments is null))
			{
				int i = this.TableView.Root.IndexOf(this.ButtonSection);
				TableSection PhotoSection = new TableSection();
				this.TableView.Root.Insert(i++, PhotoSection);

				foreach (Attachment Attachment in this.requestorIdentity.Attachments)
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

		private void AcceptButton_Clicked(object sender, EventArgs e)
		{
			App.Contracts.PetitionContractResponseAsync(this.requestedContract.ContractId, this.petitionId, this.requestorFullJid, true);
			App.ShowPage(this.owner, true);
		}

		private void DeclineButton_Clicked(object sender, EventArgs e)
		{
			App.Contracts.PetitionContractResponseAsync(this.requestedContract.ContractId, this.petitionId, this.requestorFullJid, false);
			App.ShowPage(this.owner, true);
		}

		private void IgnoreButton_Clicked(object sender, EventArgs e)
		{
			App.ShowPage(this.owner, true);
		}

		public DateTime Created => this.requestorIdentity.Created;
		public DateTime? Updated => CheckMin(this.requestorIdentity.Updated);
		public string LegalId => this.requestorIdentity.Id;
		public string State => this.requestorIdentity.State.ToString();
		public DateTime? From => CheckMin(this.requestorIdentity.From);
		public DateTime? To => CheckMin(this.requestorIdentity.To);
		public string FirstName => this.requestorIdentity["FIRST"];
		public string MiddleNames => this.requestorIdentity["MIDDLE"];
		public string LastNames => this.requestorIdentity["LAST"];
		public string PNr => this.requestorIdentity["PNR"];
		public string Address => this.requestorIdentity["ADDR"];
		public string Address2 => this.requestorIdentity["ADDR2"];
		public string PostalCode => this.requestorIdentity["ZIP"];
		public string Area => this.requestorIdentity["AREA"];
		public string City => this.requestorIdentity["CITY"];
		public string Region => this.requestorIdentity["REGION"];
		public string CountryCode => this.requestorIdentity["COUNTRY"];
		public string Country => ISO_3166_1.ToName(this.CountryCode);
		public bool IsApproved => this.requestorIdentity.State == IdentityState.Approved;
		public string Purpose => this.purpose;

		private static DateTime? CheckMin(DateTime? TP)
		{
			if (!TP.HasValue || TP.Value == DateTime.MinValue)
				return null;
			else
				return TP;
		}

		public bool BackClicked()
		{
			this.IgnoreButton_Clicked(this, new EventArgs());
			return true;
		}

	}
}
