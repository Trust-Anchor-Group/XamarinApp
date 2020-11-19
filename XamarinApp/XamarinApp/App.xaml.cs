using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Waher.Events;
using Waher.IoTGateway.Setup;
using Waher.Networking.Sniffers;
using Waher.Networking.XMPP;
using Waher.Networking.XMPP.Contracts;
using Waher.Networking.XMPP.HttpFileUpload;
using Waher.Networking.XMPP.Provisioning;
using Waher.Networking.XMPP.ServiceDiscovery;
using Waher.Persistence;
using Waher.Persistence.Files;
using Waher.Persistence.Serialization;
using Waher.Runtime.Inventory;
using Waher.Runtime.Queue;
using Waher.Security;
using XamarinApp.Connection;
using XamarinApp.MainMenu;
using XamarinApp.MainMenu.Contracts;
using XamarinApp.PersonalNumbers;
using System.Collections.Generic;
using System.Xml;

namespace XamarinApp
{
	public partial class App : Application, IDisposable
	{
		private static readonly RandomNumberGenerator rnd = RandomNumberGenerator.Create();
		private static readonly InMemorySniffer sniffer = new InMemorySniffer(250);
		private static App instance = null;

		private static Timer minuteTimer = null;
		private static XmppClient xmpp = null;
		private static ContractsClient contracts = null;
		private static HttpFileUploadClient fileUpload = null;
		private static XmppConfiguration configuration = null;
		private static string domainName = null;
		private static string accountName = null;
		private static string passwordHash = null;
		private static string passwordHashMethod = null;
		private static bool xmppSettingsOk = false;

		public App()
		{
			InitializeComponent();
			this.MainPage = new InitPage();
		}

		public void Dispose()
		{
			DisposeClient();
		}

		private static void DisposeClient()
		{
			minuteTimer?.Dispose();
			minuteTimer = null;

			contracts?.Dispose();
			contracts = null;

			fileUpload?.Dispose();
			fileUpload = null;

			xmpp?.Dispose();
			xmpp = null;
		}

		internal static InMemorySniffer Sniffer => sniffer;

		protected override void OnStart()
		{
			Thread T = new Thread(this.Initialize);     // Avoid GUI blocks.
			T.Start();
		}

		private async void Initialize()
		{
			instance = this;

			Log.Register(new InternalSink());

			AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
			{
				if (e.IsTerminating)
					Stop().Wait();
			};

			TaskScheduler.UnobservedTaskException += (sender, e) =>
			{
				e.SetObserved();
			};

			Types.Initialize(
				typeof(App).Assembly,
				typeof(Database).Assembly,
				typeof(FilesProvider).Assembly,
				typeof(ObjectSerializer).Assembly,
				typeof(XmppClient).Assembly,
				typeof(ContractsClient).Assembly,
				typeof(Waher.Things.ThingReference).Assembly,
				typeof(Waher.Runtime.Settings.RuntimeSettings).Assembly,
				typeof(Waher.Runtime.Language.Language).Assembly,
				typeof(Waher.Networking.DNS.DnsResolver).Assembly,
				typeof(Waher.Networking.XMPP.Sensor.SensorClient).Assembly,
				typeof(Waher.Networking.XMPP.Control.ControlClient).Assembly,
				typeof(Waher.Networking.XMPP.Concentrator.ConcentratorClient).Assembly,
				typeof(Waher.Networking.XMPP.P2P.XmppServerlessMessaging).Assembly,
				typeof(Waher.Networking.XMPP.Provisioning.ProvisioningClient).Assembly,
				typeof(Waher.Security.EllipticCurves.EllipticCurve).Assembly);

			string AppDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			string DataFolder = Path.Combine(AppDataFolder, "Data");

			FilesProvider Provider = await FilesProvider.CreateAsync(DataFolder, "Default", 8192, 10000, 8192, Encoding.UTF8, 10000, this.GetCustomKey);
			await Provider.RepairIfInproperShutdown(string.Empty);

			Database.Register(Provider);

			configuration = await Database.FindFirstDeleteRest<XmppConfiguration>();
			if (configuration is null)
			{
				await Task.Delay(1000);
				configuration = new XmppConfiguration();
				await Database.Insert(configuration);
			}

			PersonalNumberSchemes.Load();

			await ShowPage();
		}

		private class InternalSink : EventSink
		{
			public InternalSink()
				: base("InternalEventSink")
			{
			}

			public override Task Queue(Event Event)
			{
				return Task.CompletedTask;
			}
		}

		public static async Task ShowPage()
		{
			if (configuration.Step >= 2)
			{
				await UpdateXmpp();

				if (configuration.Step > 2 && (
					configuration.LegalIdentity is null ||
					configuration.LegalIdentity.State == IdentityState.Compromised ||
					configuration.LegalIdentity.State == IdentityState.Obsoleted ||
					configuration.LegalIdentity.State == IdentityState.Rejected))
				{
					configuration.Step = 2;
					await Database.Update(configuration);
				}

				if (configuration.Step > 4 && configuration.UsePin && string.IsNullOrEmpty(configuration.PinHash))
				{
					configuration.Step = 4;
					await Database.Update(configuration);
				}
			}

			Page Page;

			switch (configuration.Step)
			{
				case 0:
					Page = new OperatorPage(configuration);
					break;

				case 1:
					Page = new AccountPage(configuration);
					break;

				case 2:
					if (configuration.LegalIdentity is null ||
						configuration.LegalIdentity.State == IdentityState.Compromised ||
						configuration.LegalIdentity.State == IdentityState.Obsoleted ||
						configuration.LegalIdentity.State == IdentityState.Rejected)
					{
						DateTime Now = DateTime.Now;
						LegalIdentity Created = null;
						LegalIdentity Approved = null;
						bool Changed = false;

						if (await CheckServices())
						{
							foreach (LegalIdentity Identity in await App.Contracts.GetLegalIdentitiesAsync())
							{
								if (Identity.HasClientSignature &&
									Identity.HasClientPublicKey &&
									Identity.From <= Now &&
									Identity.To >= Now &&
									(Identity.State == IdentityState.Approved || Identity.State == IdentityState.Created) &&
									Identity.ValidateClientSignature())
								{
									if (Identity.State == IdentityState.Approved)
									{
										Approved = Identity;
										break;
									}
									else if (Created is null)
										Created = Identity;
								}
							}

							if (!(Approved is null))
							{
								configuration.LegalIdentity = Approved;
								Changed = true;
							}
							else if (!(Created is null))
							{
								configuration.LegalIdentity = Created;
								Changed = true;
							}

							if (Changed)
							{
								configuration.Step++;
								await Database.Update(configuration);
								Page = new Connection.IdentityPage(configuration);
								break;
							}
						}
					}

					Page = new RegisterIdentityPage(configuration);
					break;

				case 3:
					Page = new Connection.IdentityPage(configuration);
					break;

				case 4:
					Page = new DefinePinPage(configuration);
					break;

				case 5:
				default:
					Page = new MainMenuPage();
					break;
			}

			ShowPage(Page, true);
		}

		public static void ShowPage(Page Page, bool DisposeCurrent)
		{
			Device.BeginInvokeOnMainThread(() =>
			{
				Page Prev = instance.MainPage;

				instance.MainPage = Page;

				if (DisposeCurrent && Prev is IDisposable Disposable)
					Disposable.Dispose();
			});
		}

		internal static App Instance => instance;
		internal static XmppConfiguration Configuration => configuration;
		internal static Page CurrentPage => instance.MainPage;

		private async Task<KeyValuePair<byte[], byte[]>> GetCustomKey(string FileName)
		{
			byte[] Key;
			byte[] IV;
			string s;
			int i;

			try
			{
				s = await SecureStorage.GetAsync(FileName);
			}
			catch (TypeInitializationException)
			{
				// No secure storage available.

				Key = Hashes.ComputeSHA256Hash(Encoding.UTF8.GetBytes(FileName + ".Key"));
				IV = Hashes.ComputeSHA256Hash(Encoding.UTF8.GetBytes(FileName + ".IV"));
				Array.Resize<byte>(ref IV, 16);

				return new KeyValuePair<byte[], byte[]>(Key, IV);
			}

			if (!string.IsNullOrEmpty(s) && (i = s.IndexOf(',')) > 0)
			{
				Key = Hashes.StringToBinary(s.Substring(0, i));
				IV = Hashes.StringToBinary(s.Substring(i + 1));
			}
			else
			{
				Key = new byte[32];
				IV = new byte[16];

				lock (rnd)
				{
					rnd.GetBytes(Key);
					rnd.GetBytes(IV);
				}

				s = Hashes.BinaryToString(Key) + "," + Hashes.BinaryToString(IV);
				await SecureStorage.SetAsync(FileName, s);
			}

			return new KeyValuePair<byte[], byte[]>(Key, IV);
		}

		public static byte[] GetBytes(int NrBytes)
		{
			byte[] Result = new byte[NrBytes];

			lock (rnd)
			{
				rnd.GetBytes(Result);
			}

			return Result;
		}

		public static int GetNext(int MaxExclusive)
		{
			if (MaxExclusive <= 0)
				throw new ArgumentException("Must be positive.", nameof(MaxExclusive));

			byte[] A = new byte[4];
			int i = MaxExclusive;
			int c = 0;

			while (i > 0)
			{
				i >>= 1;
				c++;
			}

			while (c > 0)
			{
				i <<= 1;
				i |= 1;
				c--;
			}

			lock (rnd)
			{
				do
				{
					rnd.GetBytes(A);
					c = BitConverter.ToInt32(A, 0);
					c &= i;
				}
				while (c >= MaxExclusive);
			}

			return c;
		}

		public static bool Online
		{
			get
			{
				return !(xmpp is null) && xmpp.State == XmppState.Connected;
			}
		}

		public static XmppClient Xmpp => xmpp;

		public static ContractsClient Contracts
		{
			get => contracts;
		}

		public static HttpFileUploadClient FileUpload
		{
			get => fileUpload;
		}

		private static async Task UpdateXmpp()
		{
			if (xmpp is null ||
				domainName != configuration.Domain ||
				accountName != configuration.Account ||
				passwordHash != configuration.PasswordHash ||
				passwordHashMethod != configuration.PasswordHashMethod)
			{
				DisposeClient();

				domainName = configuration.Domain;
				accountName = configuration.Account;
				passwordHash = configuration.PasswordHash;
				passwordHashMethod = configuration.PasswordHashMethod;

				(string HostName, int PortNumber) = await OperatorPage.GetXmppClientService(domainName);

				xmpp = new XmppClient(HostName, PortNumber, accountName, passwordHash, passwordHashMethod, "en",
					typeof(App).Assembly, sniffer)
				{
					TrustServer = false,
					AllowCramMD5 = false,
					AllowDigestMD5 = false,
					AllowPlain = false,
					AllowEncryption = true,
					AllowScramSHA1 = true,
					AllowScramSHA256 = true
				};

				xmpp.OnStateChanged += (sender2, NewState) =>
				{
					switch (NewState)
					{
						case XmppState.Connected:
							xmppSettingsOk = true;

							minuteTimer?.Dispose();
							minuteTimer = null;

							minuteTimer = new Timer(MinuteTick, null, 60000, 60000);


							if (string.IsNullOrEmpty(configuration.LegalJid) ||
								string.IsNullOrEmpty(configuration.RegistryJid) ||
								string.IsNullOrEmpty(configuration.ProvisioningJid) ||
								string.IsNullOrEmpty(configuration.HttpFileUploadJid))
							{
								Task _ = FindServices(xmpp);
							}
							break;

						case XmppState.Error:
							if (xmppSettingsOk)
							{
								xmppSettingsOk = false;
								xmpp?.Reconnect();
							}
							break;
					}

					if (instance?.MainPage is IConnectionStateChanged ConnectionStateChanged)
						Device.BeginInvokeOnMainThread(() => ConnectionStateChanged.ConnectionStateChanged(NewState));

					return Task.CompletedTask;
				};

				xmpp.Connect(domainName);
				minuteTimer = new Timer(MinuteTick, null, 60000, 60000);

				await AddLegalService(configuration.LegalJid);
				AddFileUploadService(configuration.HttpFileUploadJid, configuration.HttpFileUploadMaxSize);
			}
		}

		internal static async Task<bool> CheckServices()
		{
			if (string.IsNullOrEmpty(configuration.LegalJid) ||
				string.IsNullOrEmpty(configuration.HttpFileUploadJid) ||
				!configuration.HttpFileUploadMaxSize.HasValue)
			{
				TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>();

				Task h(object sender, XmppState NewState)
				{
					if (NewState == XmppState.Connected || NewState == XmppState.Error)
						Done.TrySetResult(true);

					return Task.CompletedTask;
				}

				try
				{
					App.Xmpp.OnStateChanged += h;

					if (App.Xmpp.State == XmppState.Connected || App.Xmpp.State == XmppState.Error)
						Done.TrySetResult(true);

					Task _ = Task.Delay(10000).ContinueWith((T) => Done.TrySetResult(false));

					if (await Done.Task)
						return await FindServices(App.xmpp);
					else
						return false;
				}
				finally
				{
					App.Xmpp.OnStateChanged -= h;
				}
			}
			else
				return true;
		}

		internal static async Task<bool> FindServices(XmppClient Client)
		{
			ServiceItemsDiscoveryEventArgs e2 = await Client.ServiceItemsDiscoveryAsync(null, string.Empty, string.Empty);
			bool Changed = false;

			foreach (Item Item in e2.Items)
			{
				ServiceDiscoveryEventArgs e3 = await Client.ServiceDiscoveryAsync(null, Item.JID, Item.Node);

				if (e3.HasFeature(ContractsClient.NamespaceLegalIdentities) &&
					e3.HasFeature(ContractsClient.NamespaceLegalIdentities))
				{
					if (configuration.LegalJid != Item.JID)
					{
						configuration.LegalJid = Item.JID;
						Changed = true;
					}
				}

				if (e3.HasFeature(ThingRegistryClient.NamespaceDiscovery))
				{
					if (configuration.RegistryJid != Item.JID)
					{
						configuration.RegistryJid = Item.JID;
						Changed = true;
					}
				}

				if (e3.HasFeature(ProvisioningClient.NamespaceProvisioningDevice) &&
					e3.HasFeature(ProvisioningClient.NamespaceProvisioningOwner) &&
					e3.HasFeature(ProvisioningClient.NamespaceProvisioningToken))
				{
					if (configuration.ProvisioningJid != Item.JID)
					{
						configuration.ProvisioningJid = Item.JID;
						Changed = true;
					}
				}

				if (e3.HasFeature(HttpFileUploadClient.Namespace))
				{
					if (configuration.HttpFileUploadJid != Item.JID)
					{
						configuration.HttpFileUploadJid = Item.JID;
						Changed = true;
					}

					long? MaxSize = HttpFileUploadClient.FindMaxFileSize(Client, e3);

					if (configuration.HttpFileUploadMaxSize != MaxSize)
					{
						configuration.HttpFileUploadMaxSize = MaxSize;
						Changed = true;
					}
				}
			}

			if (Changed)
				await Database.Update(configuration);

			bool Result = true;

			if (!string.IsNullOrEmpty(configuration.LegalJid))
				await App.AddLegalService(configuration.LegalJid);
			else
				Result = false;

			if (!string.IsNullOrEmpty(configuration.HttpFileUploadJid) && configuration.HttpFileUploadMaxSize.HasValue)
				App.AddFileUploadService(configuration.HttpFileUploadJid, configuration.HttpFileUploadMaxSize);
			else
				Result = false;

			return Result;
		}

		internal static async Task AddLegalService(string JID)
		{
			if (!string.IsNullOrEmpty(JID) && !(xmpp is null))
			{
				contracts = await ContractsClient.Create(xmpp, JID);
				contracts.IdentityUpdated += Contracts_IdentityUpdated;

				contracts.PetitionForIdentityReceived += Contracts_PetitionForIdentityReceived;
				contracts.PetitionedIdentityResponseReceived += Contracts_PetitionedIdentityResponseReceived;

				contracts.PetitionForContractReceived += Contracts_PetitionForContractReceived;
				contracts.PetitionedContractResponseReceived += Contracts_PetitionedContractResponseReceived;

				contracts.PetitionForSignatureReceived += Contracts_PetitionForSignatureReceived;
				contracts.PetitionedSignatureResponseReceived += Contracts_PetitionedSignatureResponseReceived;

				contracts.PetitionForPeerReviewIDReceived += Contracts_PetitionForPeerReviewIDReceived;
				contracts.PetitionedPeerReviewIDResponseReceived += Contracts_PetitionedPeerReviewIDResponseReceived;
			}
		}

		private static async Task Contracts_PetitionedPeerReviewIDResponseReceived(object Sender, SignaturePetitionResponseEventArgs e)
		{
			try
			{
				if (!e.Response)
					App.DisplayMessage("Peer Review rejected", "A peer you requested to review your application, has rejected it.");
				else
				{
					StringBuilder Xml = new StringBuilder();
					configuration.LegalIdentity.Serialize(Xml, true, true, true, true, true, true, true);
					byte[] Data = Encoding.UTF8.GetBytes(Xml.ToString());

					bool? Result = contracts.ValidateSignature(e.RequestedIdentity, Data, e.Signature);
					if (!Result.HasValue || !Result.Value)
						App.DisplayMessage("Peer Review rejected", "A peer review you requested has been rejected, due to a signature error.");
					else
					{
						await contracts.AddPeerReviewIDAttachment(configuration.LegalIdentity, e.RequestedIdentity, e.Signature);
						App.DisplayMessage("Peer Review accepted", "A peer review you requested has been accepted.");
					}
				}
			}
			catch (Exception ex)
			{
				App.DisplayMessage(ex);
			}
		}

		private static Task Contracts_PetitionForPeerReviewIDReceived(object Sender, SignaturePetitionEventArgs e)
		{
			ShowPage(new MainMenu.IdentityPage(configuration, instance.MainPage, e.RequestorIdentity, e), false);
			return Task.CompletedTask;
		}

		private static Task Contracts_PetitionedSignatureResponseReceived(object Sender, SignaturePetitionResponseEventArgs e)
		{
			return Task.CompletedTask;
		}

		private static Task Contracts_PetitionForSignatureReceived(object Sender, SignaturePetitionEventArgs e)
		{
			// Reject all signature requests by default:
			return contracts.PetitionSignatureResponseAsync(e.SignatoryIdentityId, e.ContentToSign, new byte[0], e.PetitionId, e.RequestorFullJid, false);
		}

		internal static void AddFileUploadService(string JID, long? MaxFileSize)
		{
			if (!string.IsNullOrEmpty(JID) && MaxFileSize.HasValue && !(xmpp is null))
				fileUpload = new HttpFileUploadClient(xmpp, JID, MaxFileSize);
		}

		private static Task Contracts_PetitionedContractResponseReceived(object Sender, ContractPetitionResponseEventArgs e)
		{
			if (!e.Response || e.RequestedContract is null)
				App.DisplayMessage("Message", "Petition to view contract was denied.");
			else
				ShowPage(new ViewContractPage(configuration, instance.MainPage, e.RequestedContract, false), false);

			return Task.CompletedTask;
		}

		private static async Task Contracts_PetitionForContractReceived(object Sender, ContractPetitionEventArgs e)
		{
			try
			{
				Contract Contract = await contracts.GetContractAsync(e.RequestedContractId);

				if (Contract.State == ContractState.Deleted ||
					Contract.State == ContractState.Rejected)
				{
					await contracts.PetitionContractResponseAsync(e.RequestedContractId, e.PetitionId, e.RequestorFullJid, false);
				}
				else
				{
					ShowPage(new PetitionContractPage(configuration, instance.MainPage, e.RequestorIdentity, e.RequestorFullJid,
						Contract, e.PetitionId, e.Purpose), false);
				}
			}
			catch (Exception)
			{
				await contracts.PetitionContractResponseAsync(e.RequestedContractId, e.PetitionId, e.RequestorFullJid, false);
			}
		}

		private static Task Contracts_PetitionedIdentityResponseReceived(object Sender, LegalIdentityPetitionResponseEventArgs e)
		{
			if (!e.Response || e.RequestedIdentity is null)
				App.DisplayMessage("Message", "Petition to view legal identity was denied.");
			else
				ShowPage(new MainMenu.IdentityPage(configuration, instance.MainPage, e.RequestedIdentity), false);

			return Task.CompletedTask;
		}

		private static async Task Contracts_PetitionForIdentityReceived(object Sender, LegalIdentityPetitionEventArgs e)
		{
			try
			{
				LegalIdentity Identity;

				if (e.RequestedIdentityId == configuration.LegalIdentity.Id)
					Identity = configuration.LegalIdentity;
				else
					Identity = await contracts.GetLegalIdentityAsync(e.RequestedIdentityId);

				if (Identity.State == IdentityState.Compromised ||
					Identity.State == IdentityState.Rejected)
				{
					await contracts.PetitionIdentityResponseAsync(e.RequestedIdentityId, e.PetitionId, e.RequestorFullJid, false);
				}
				else
				{
					ShowPage(new PetitionIdentityPage(instance.MainPage, e.RequestorIdentity, e.RequestorFullJid,
						e.RequestedIdentityId, e.PetitionId, e.Purpose), false);
				}
			}
			catch (Exception)
			{
				await contracts.PetitionIdentityResponseAsync(e.RequestedIdentityId, e.PetitionId, e.RequestorFullJid, false);
			}
		}

		private static async Task Contracts_IdentityUpdated(object Sender, LegalIdentityEventArgs e)
		{
			if (configuration is null)
				return;

			if (configuration.LegalIdentity is null ||
				configuration.LegalIdentity.Id == e.Identity.Id ||
				configuration.LegalIdentity.Created < e.Identity.Created)
			{
				configuration.LegalIdentity = e.Identity;
				await Database.Update(configuration);

				if (instance.MainPage is ILegalIdentityChanged LegalIdentityChanged)
					Device.BeginInvokeOnMainThread(() => LegalIdentityChanged.LegalIdentityChanged(e.Identity));
			}
		}

		private static void MinuteTick(object State)
		{
			if (!(xmpp is null) && (xmpp.State == XmppState.Error || xmpp.State == XmppState.Offline))
				xmpp.Reconnect();
		}

		protected override void OnSleep()
		{
			xmpp?.SetPresence(Availability.Away);
		}

		protected override void OnResume()
		{
			xmpp?.SetPresence(Availability.Online);
		}

		internal async Task Stop()
		{
			try
			{
				instance = null;

				await Types.StopAllModules();

				minuteTimer?.Dispose();
				minuteTimer = null;

				contracts?.Dispose();
				contracts = null;

				fileUpload?.Dispose();
				fileUpload = null;

				if (!(xmpp is null))
				{
					TaskCompletionSource<bool> OfflineSent = new TaskCompletionSource<bool>();

					xmpp.SetPresence(Availability.Offline, (sender, e) => OfflineSent.TrySetResult(true));
					Task _ = Task.Delay(1000).ContinueWith((T) => OfflineSent.TrySetResult(false));

					await OfflineSent.Task;

					xmpp.Dispose();
					xmpp = null;
				}

				Log.Terminate();
			}
			finally
			{
				await Waher.Persistence.LifeCycle.DatabaseModule.Flush();

				ICloseApplication CloseApp = DependencyService.Get<ICloseApplication>();
				CloseApp?.CloseApplication();
			}
		}

		public static async Task OpenLegalIdentity(string LegalId, string Purpose)
		{
			try
			{
				LegalIdentity Identity = await App.Contracts.GetLegalIdentityAsync(LegalId);
				App.ShowPage(new MainMenu.IdentityPage(configuration, instance.MainPage, Identity), true);
			}
			catch (Exception)
			{
				await App.Contracts.PetitionIdentityAsync(LegalId, Guid.NewGuid().ToString(), Purpose);

				App.DisplayMessage("Petition Sent", "A petition has been sent to the owner of the identity. " +
					"If the owner accepts the petition, the identity information will be displayed on the screen.");
			}
		}

		public static async Task OpenContract(string ContractId, string Purpose)
		{
			try
			{
				Contract Contract = await App.Contracts.GetContractAsync(ContractId);

				if (Contract.CanActAsTemplate && Contract.State == ContractState.Approved)
					App.ShowPage(new NewContractPage(configuration, instance.MainPage, Contract), true);
				else
					App.ShowPage(new ViewContractPage(configuration, instance.MainPage, Contract, false), true);
			}
			catch (Exception)
			{
				await App.Contracts.PetitionContractAsync(ContractId, Guid.NewGuid().ToString(), Purpose);

				App.DisplayMessage("Petition Sent", "A petition has been sent to the parts of the contract. " +
					"If any of the parts accepts the petition, the contract information will be displayed on the screen.");
			}
		}

		public static bool Back()
		{
			if (instance.MainPage is IBackButton Page)
				return Page.BackClicked();
			else
				return false;
		}

		internal static string SnifferToText()
		{
			StringBuilder sb = new StringBuilder();

			using (StringWriter Writer = new StringWriter(sb))
			{
				using (TextWriterSniffer Output = new TextWriterSniffer(Writer, BinaryPresentationMethod.ByteCount))
				{
					sniffer?.Replay(Output);
				}
			}

			return sb.ToString();
		}

		internal static string SnifferToXml()
		{
			XmlWriterSettings Settings = new XmlWriterSettings()
			{
				Encoding = Encoding.UTF8,
				Indent = true,
				IndentChars = "\t",
				NewLineChars = "\r\n",
				NewLineHandling = NewLineHandling.Entitize,
				NewLineOnAttributes = false
			};

			return SnifferToXml(Settings);
		}

		internal static string SnifferToXml(XmlWriterSettings Settings)
		{
			StringBuilder sb = new StringBuilder();

			using (XmlWriter Writer = XmlWriter.Create(sb, Settings))
			{
				using (XmlWriterSniffer Output = new XmlWriterSniffer(Writer, BinaryPresentationMethod.ByteCount))
				{
					sniffer?.Replay(Output);
				}
			}

			return sb.ToString();
		}

		internal static void SaveSnifferAsText(string FileName)
		{
			using (TextFileSniffer Output = new TextFileSniffer(FileName, BinaryPresentationMethod.ByteCount))
			{
				sniffer?.Replay(Output);
			}
		}

		internal static void SaveSnifferAsXml(string FileName)
		{
			using (XmlFileSniffer Output = new XmlFileSniffer(FileName, BinaryPresentationMethod.ByteCount))
			{
				sniffer?.Replay(Output);
			}
		}

		internal static void SaveSnifferAsXml(string FileName, string XslTransform)
		{
			using (XmlFileSniffer Output = new XmlFileSniffer(FileName, XslTransform, BinaryPresentationMethod.ByteCount))
			{
				sniffer?.Replay(Output);
			}
		}

		internal static void DisplayMessage(string Header, string Message)
		{
			messageQueue.Add(new MessageRec()
			{
				Header = Header,
				Message = Message
			});

			if (!displayingMessages)
				Device.BeginInvokeOnMainThread(async () => await DisplayMessages());
		}

		internal static void DisplayMessage(Exception Exception)
		{
			Exception = Log.UnnestException(Exception);

			if (Exception is AggregateException AggregateException)
			{
				foreach (Exception Exception2 in AggregateException.InnerExceptions)
					DisplayMessage(Exception2);
			}
			else
				DisplayMessage("Error", Exception.Message);
		}

		private static async Task DisplayMessages()
		{
			MessageRec Rec;

			displayingMessages = true;
			try
			{
				do
				{
					Rec = await messageQueue.Wait();
					await instance.MainPage.DisplayAlert(Rec.Header, Rec.Message, "OK");
				}
				while (messageQueue.CountItems > 0);
			}
			finally
			{
				displayingMessages = false;
			}
		}

		private static readonly AsyncQueue<MessageRec> messageQueue = new AsyncQueue<MessageRec>();
		private static bool displayingMessages = false;

		private class MessageRec
		{
			public string Header;
			public string Message;
		}

	}
}
