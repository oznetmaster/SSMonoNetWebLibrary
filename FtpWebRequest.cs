//
// System.Net.FtpWebRequest.cs
//
// Authors:
//	Carlos Alberto Cortez (calberto.cortez@gmail.com)
//
// (c) Copyright 2006 Novell, Inc. (http://www.novell.com)
// (c) Copyright 2018 Nivloc Enterprises Ltd
//

using System;
#if SSHARP
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronIO;
using SSMono.Security.Cryptography.X509Certificates;
using SSMono.Net.Cache;
using SSMono.Threading;
using IAsyncResult = Crestron.SimplSharp.CrestronIO.IAsyncResult;
using AsyncCallback = Crestron.SimplSharp.CrestronIO.AsyncCallback;
using Socket = Crestron.SimplSharp.CrestronSockets.CrestronSocket;
using SSMono.Net.Sockets;
using SocketException = SSMono.Net.Sockets.SocketException;
using Crestron.SimplSharp.CrestronSockets;
using SSMono.Net.Security;
using SSMono.Security.Authentication;
#else
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Net.Cache;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
#endif
using System.Text;

#if SSHARP
namespace SSMono.Net
#else
namespace System.Net
#endif
	{
	public sealed class FtpWebRequest : WebRequest
		{
		private Uri requestUri;
		private string file_name; // By now, used for upload
		private ServicePoint servicePoint;
		private Stream origDataStream;
		private Stream dataStream;
		private Stream controlStream;
		private StreamReader controlReader;
		private NetworkCredential credentials;
		private IPHostEntry hostEntry;
		private IPEndPoint localEndPoint;
		private IWebProxy proxy;
		private int timeout = 100000;
		private int rwTimeout = 300000;
		private long offset = 0;
		private bool binary = true;
		private bool enableSsl = false;
		private bool usePassive = true;
		private bool keepAlive = false;
		private string method = WebRequestMethods.Ftp.DownloadFile;
		private string renameTo;
		private object locker = new object ();

		private RequestState requestState = RequestState.Before;
		private FtpAsyncResult asyncResult;
		private FtpWebResponse ftpResponse;
		private Stream requestStream;
		private string initial_path;

		private const string ChangeDir = "CWD";
		private const string UserCommand = "USER";
		private const string PasswordCommand = "PASS";
		private const string TypeCommand = "TYPE";
		private const string PassiveCommand = "PASV";
		private const string PortCommand = "PORT";
		private const string AbortCommand = "ABOR";
		private const string AuthCommand = "AUTH";
		private const string RestCommand = "REST";
		private const string RenameFromCommand = "RNFR";
		private const string RenameToCommand = "RNTO";
		private const string QuitCommand = "QUIT";
		private const string EOL = "\r\n"; // Special end of line

		private enum RequestState
			{
			Before,
			Scheduled,
			Connecting,
			Authenticating,
			OpeningData,
			TransferInProgress,
			Finished,
			Aborted,
			Error
			}

		// sorted commands
		private static readonly string[] supportedCommands = new string[]
			{
			WebRequestMethods.Ftp.AppendFile, // APPE
			WebRequestMethods.Ftp.DeleteFile, // DELE
			WebRequestMethods.Ftp.ListDirectoryDetails, // LIST
			WebRequestMethods.Ftp.GetDateTimestamp, // MDTM
			WebRequestMethods.Ftp.MakeDirectory, // MKD
			WebRequestMethods.Ftp.ListDirectory, // NLST
			WebRequestMethods.Ftp.PrintWorkingDirectory, // PWD
			WebRequestMethods.Ftp.Rename, // RENAME
			WebRequestMethods.Ftp.DownloadFile, // RETR
			WebRequestMethods.Ftp.RemoveDirectory, // RMD
			WebRequestMethods.Ftp.GetFileSize, // SIZE
			WebRequestMethods.Ftp.UploadFile, // STOR
			WebRequestMethods.Ftp.UploadFileWithUniqueName // STUR
			};

		internal FtpWebRequest (Uri uri)
			{
			this.requestUri = uri;
			this.proxy = GlobalProxySelection.Select;
			}

		private static Exception GetMustImplement ()
			{
			return new NotImplementedException ();
			}

		[MonoTODO]
		public X509CertificateCollection ClientCertificates
			{
			get { throw GetMustImplement (); }
			set { throw GetMustImplement (); }
			}

		[MonoTODO]
		public override string ConnectionGroupName
			{
			get { throw GetMustImplement (); }
			set { throw GetMustImplement (); }
			}

		public override string ContentType
			{
			get { throw new NotSupportedException (); }
			set { throw new NotSupportedException (); }
			}

		public override long ContentLength
			{
			get { return 0; }
			set
				{
				// DO nothing
				}
			}

		public long ContentOffset
			{
			get { return offset; }
			set
				{
				CheckRequestStarted ();
				if (value < 0)
					throw new ArgumentOutOfRangeException ();

				offset = value;
				}
			}

		public override ICredentials Credentials
			{
			get { return credentials; }
			set
				{
				CheckRequestStarted ();
				if (value == null)
					throw new ArgumentNullException ();
				if (!(value is NetworkCredential))
					throw new ArgumentException ();

				credentials = value as NetworkCredential;
				}
			}

#if !NET_2_1
		[MonoTODO]
		public new static RequestCachePolicy DefaultCachePolicy
			{
			get { throw GetMustImplement (); }
			set { throw GetMustImplement (); }
			}
#endif

		public bool EnableSsl
			{
			get { return enableSsl; }
			set
				{
				CheckRequestStarted ();
				enableSsl = value;
				}
			}

		[MonoTODO]
		public override WebHeaderCollection Headers
			{
			get { throw GetMustImplement (); }
			set { throw GetMustImplement (); }
			}

		[MonoTODO ("We don't support KeepAlive = true")]
		public bool KeepAlive
			{
			get { return keepAlive; }
			set
				{
				CheckRequestStarted ();
				//keepAlive = value;
				}
			}

		public override string Method
			{
			get { return method; }
			set
				{
				CheckRequestStarted ();
				if (value == null)
					throw new ArgumentNullException ("Method string cannot be null");

				if (value.Length == 0 || Array.BinarySearch (supportedCommands, value) < 0)
					throw new ArgumentException ("Method not supported", "value");

				method = value;
				}
			}

		public override bool PreAuthenticate
			{
			get { throw new NotSupportedException (); }
			set { throw new NotSupportedException (); }
			}

		public override IWebProxy Proxy
			{
			get { return proxy; }
			set
				{
				CheckRequestStarted ();
				proxy = value;
				}
			}

		public int ReadWriteTimeout
			{
			get { return rwTimeout; }
			set
				{
				CheckRequestStarted ();

				if (value < - 1)
					throw new ArgumentOutOfRangeException ();
				else
					rwTimeout = value;
				}
			}

		public string RenameTo
			{
			get { return renameTo; }
			set
				{
				CheckRequestStarted ();
				if (value == null || value.Length == 0)
					throw new ArgumentException ("RenameTo value can't be null or empty", "RenameTo");

				renameTo = value;
				}
			}

		public override Uri RequestUri
			{
			get { return requestUri; }
			}

		public ServicePoint ServicePoint
			{
			get { return GetServicePoint (); }
			}

		public bool UsePassive
			{
			get { return usePassive; }
			set
				{
				CheckRequestStarted ();
				usePassive = value;
				}
			}

		[MonoTODO]
		public override bool UseDefaultCredentials
			{
			get { throw GetMustImplement (); }
			set { throw GetMustImplement (); }
			}

		public bool UseBinary
			{
			get { return binary; }
			set
				{
				CheckRequestStarted ();
				binary = value;
				}
			}

		public override int Timeout
			{
			get { return timeout; }
			set
				{
				CheckRequestStarted ();

				if (value < -1)
					throw new ArgumentOutOfRangeException ();
				else
					timeout = value;
				}
			}

		private string DataType
			{
			get { return binary ? "I" : "A"; }
			}

		private RequestState State
			{
			get
				{
				lock (locker)
					{
					return requestState;
					}
				}

			set
				{
				lock (locker)
					{
					CheckIfAborted ();
					CheckFinalState ();
					requestState = value;
					}
				}
			}

		public override void Abort ()
			{
			lock (locker)
				{
				if (State == RequestState.TransferInProgress)
					{
					/*FtpStatus status = */
					SendCommand (false, AbortCommand);
					}

				if (!InFinalState ())
					{
					State = RequestState.Aborted;
					ftpResponse = new FtpWebResponse (this, requestUri, method, FtpStatusCode.FileActionAborted, "Aborted by request");
					}
				}
			}

		public override IAsyncResult BeginGetResponse (AsyncCallback callback, object state)
			{
			if (asyncResult != null && !asyncResult.IsCompleted)
				throw new InvalidOperationException ("Cannot re-call BeginGetRequestStream/BeginGetResponse while a previous call is still in progress");

			CheckIfAborted ();

			asyncResult = new FtpAsyncResult (callback, state);

			lock (locker)
				{
				if (InFinalState ())
					asyncResult.SetCompleted (true, ftpResponse);
				else
					{
					if (State == RequestState.Before)
						State = RequestState.Scheduled;

#if SSHARP
					ThreadPool.QueueUserWorkItem (ProcessRequest);
#else
					Thread thread = new Thread (ProcessRequest);
					thread.Start ();
#endif
					}
				}

			return asyncResult;
			}

		public override WebResponse EndGetResponse (IAsyncResult asyncResult)
			{
			if (asyncResult == null)
				throw new ArgumentNullException ("AsyncResult cannot be null!");

			if (!(asyncResult is FtpAsyncResult) || asyncResult != this.asyncResult)
				throw new ArgumentException ("AsyncResult is from another request!");

			FtpAsyncResult asyncFtpResult = (FtpAsyncResult)asyncResult;
			if (!asyncFtpResult.WaitUntilComplete (timeout, false))
				{
				Abort ();
				throw new WebException ("Transfer timed out.", WebExceptionStatus.Timeout);
				}

			CheckIfAborted ();

			asyncResult = null;

			if (asyncFtpResult.GotException)
				throw asyncFtpResult.Exception;

			return asyncFtpResult.Response;
			}

		public override WebResponse GetResponse ()
			{
			IAsyncResult asyncResult = BeginGetResponse (null, null);
			return EndGetResponse (asyncResult);
			}

		public override IAsyncResult BeginGetRequestStream (AsyncCallback callback, object state)
			{
			if (method != WebRequestMethods.Ftp.UploadFile && method != WebRequestMethods.Ftp.UploadFileWithUniqueName && method != WebRequestMethods.Ftp.AppendFile)
				throw new ProtocolViolationException ();

			lock (locker)
				{
				CheckIfAborted ();

				if (State != RequestState.Before)
					throw new InvalidOperationException ("Cannot re-call BeginGetRequestStream/BeginGetResponse while a previous call is still in progress");

				State = RequestState.Scheduled;
				}

			asyncResult = new FtpAsyncResult (callback, state);
#if SSHARP
			ThreadPool.QueueUserWorkItem (ProcessRequest);
#else
					Thread thread = new Thread (ProcessRequest);
					thread.Start ();
#endif

			return asyncResult;
			}

		public override Stream EndGetRequestStream (IAsyncResult asyncResult)
			{
			if (asyncResult == null)
				throw new ArgumentNullException ("asyncResult");

			if (!(asyncResult is FtpAsyncResult))
				throw new ArgumentException ("asyncResult");

			if (State == RequestState.Aborted)
				throw new WebException ("Request aborted", WebExceptionStatus.RequestCanceled);

			if (asyncResult != this.asyncResult)
				throw new ArgumentException ("AsyncResult is from another request!");

			FtpAsyncResult res = (FtpAsyncResult)asyncResult;

			if (!res.WaitUntilComplete (timeout, false))
				{
				Abort ();
				throw new WebException ("Request timed out");
				}

			if (res.GotException)
				throw res.Exception;

			return res.Stream;
			}

		public override Stream GetRequestStream ()
			{
			IAsyncResult asyncResult = BeginGetRequestStream (null, null);
			return EndGetRequestStream (asyncResult);
			}

		private ServicePoint GetServicePoint ()
			{
			if (servicePoint == null)
				servicePoint = ServicePointManager.FindServicePoint (requestUri, proxy);

			return servicePoint;
			}

		// Probably move some code of command connection here
		private void ResolveHost ()
			{
			CheckIfAborted ();
			hostEntry = GetServicePoint ().HostEntry;

			if (hostEntry == null)
				{
				ftpResponse.UpdateStatus (new FtpStatus (FtpStatusCode.ActionAbortedLocalProcessingError, "Cannot resolve server name"));
				throw new WebException ("The remote server name could not be resolved: " + requestUri, null, WebExceptionStatus.NameResolutionFailure, ftpResponse);
				}
			}

#if SSHARP
		private void ProcessRequest (object state)
#else
		private void ProcessRequest ()
#endif
			{
			if (State == RequestState.Scheduled)
				{
				ftpResponse = new FtpWebResponse (this, requestUri, method, keepAlive);

				try
					{
					ProcessMethod ();
					//State = RequestState.Finished;
					//finalResponse = ftpResponse;
					asyncResult.SetCompleted (false, ftpResponse);
					}
				catch (Exception e)
					{
					if (!GetServicePoint ().UsesProxy)
						State = RequestState.Error;
					SetCompleteWithError (e);
					}
				}
			else
				{
				if (InProgress ())
					{
					FtpStatus status = GetResponseStatus ();

					ftpResponse.UpdateStatus (status);

					if (ftpResponse.IsFinal ())
						State = RequestState.Finished;
					}

				asyncResult.SetCompleted (false, ftpResponse);
				}
			}

		private void SetType ()
			{
			if (binary)
				{
				FtpStatus status = SendCommand (TypeCommand, DataType);
				if ((int)status.StatusCode < 200 || (int)status.StatusCode >= 300)
					throw CreateExceptionFromResponse (status);
				}
			}

		private string GetRemoteFolderPath (Uri uri)
			{
			string result;
			string local_path = Uri.UnescapeDataString (uri.LocalPath);
			if (initial_path == null || initial_path == "/")
				result = local_path;
			else
				{
				if (local_path[0] == '/')
					local_path = local_path.Substring (1);

				UriBuilder initialBuilder = new UriBuilder ()
					{
					Scheme = "ftp",
					Host = "dummy-host",
					Path = initial_path,
					};
				Uri initial = initialBuilder.Uri;
				result = new Uri (initial, local_path).LocalPath;
				}

			int last = result.LastIndexOf ('/');
			if (last == -1)
				return null;

			return result.Substring (0, last + 1);
			}

		private void CWDAndSetFileName (Uri uri)
			{
			string remote_folder = GetRemoteFolderPath (uri);
			FtpStatus status;
			if (remote_folder != null)
				{
				status = SendCommand (ChangeDir, remote_folder);
				if ((int)status.StatusCode < 200 || (int)status.StatusCode >= 300)
					throw CreateExceptionFromResponse (status);

				int last = uri.LocalPath.LastIndexOf ('/');
				if (last >= 0)
					file_name = Uri.UnescapeDataString (uri.LocalPath.Substring (last + 1));
				}
			}

		private void ProcessMethod ()
			{
			ServicePoint sp = GetServicePoint ();
			if (sp.UsesProxy)
				{
				if (method != WebRequestMethods.Ftp.DownloadFile)
					throw new NotSupportedException ("FTP+proxy only supports RETR");

				HttpWebRequest req = (HttpWebRequest)WebRequest.Create (proxy.GetProxy (requestUri));
				req.Address = requestUri;
				requestState = RequestState.Finished;
				WebResponse response = req.GetResponse ();
				ftpResponse.Stream = new FtpDataStream (this, response.GetResponseStream (), true);
				ftpResponse.StatusCode = FtpStatusCode.CommandOK;
				return;
				}
			State = RequestState.Connecting;

			ResolveHost ();

			OpenControlConnection ();
			CWDAndSetFileName (requestUri);
			SetType ();

			switch (method)
				{
					// Open data connection and receive data
				case WebRequestMethods.Ftp.DownloadFile:
				case WebRequestMethods.Ftp.ListDirectory:
				case WebRequestMethods.Ftp.ListDirectoryDetails:
					DownloadData ();
					break;
					// Open data connection and send data
				case WebRequestMethods.Ftp.AppendFile:
				case WebRequestMethods.Ftp.UploadFile:
				case WebRequestMethods.Ftp.UploadFileWithUniqueName:
					UploadData ();
					break;
					// Get info from control connection
				case WebRequestMethods.Ftp.GetFileSize:
				case WebRequestMethods.Ftp.GetDateTimestamp:
				case WebRequestMethods.Ftp.PrintWorkingDirectory:
				case WebRequestMethods.Ftp.MakeDirectory:
				case WebRequestMethods.Ftp.Rename:
				case WebRequestMethods.Ftp.DeleteFile:
					ProcessSimpleMethod ();
					break;
				default: // What to do here?
					throw new Exception (String.Format ("Support for command {0} not implemented yet", method));
				}

			CheckIfAborted ();
			}

		private void CloseControlConnection ()
			{
			if (controlStream != null)
				{
				SendCommand (QuitCommand);
				controlStream.Close ();
				controlStream = null;
				}
			}

		internal void CloseDataConnection ()
			{
			if (origDataStream != null)
				{
				origDataStream.Close ();
				origDataStream = null;
				}
			}

		private void CloseConnection ()
			{
			CloseControlConnection ();
			CloseDataConnection ();
			}

		private void ProcessSimpleMethod ()
			{
			State = RequestState.TransferInProgress;

			FtpStatus status;

			if (method == WebRequestMethods.Ftp.PrintWorkingDirectory)
				method = "PWD";

			if (method == WebRequestMethods.Ftp.Rename)
				method = RenameFromCommand;

			status = SendCommand (method, file_name);

			ftpResponse.Stream = Stream.Null;

			string desc = status.StatusDescription;

			switch (method)
				{
				case WebRequestMethods.Ftp.GetFileSize:
					{
					if (status.StatusCode != FtpStatusCode.FileStatus)
						throw CreateExceptionFromResponse (status);

					int i, len;
					long size;
					for (i = 4, len = 0; i < desc.Length && Char.IsDigit (desc[i]); i++, len++)
						;

					if (len == 0)
						throw new WebException ("Bad format for server response in " + method);

#if SSHARP
					if (!TryParsers.Int64TryParse (desc.Substring (4, len), out size))
#else
					if (!Int64.TryParse (desc.Substring (4, len), out size))
#endif
						throw new WebException ("Bad format for server response in " + method);

					ftpResponse.contentLength = size;
					}
					break;
				case WebRequestMethods.Ftp.GetDateTimestamp:
					if (status.StatusCode != FtpStatusCode.FileStatus)
						throw CreateExceptionFromResponse (status);
					ftpResponse.LastModified = DateTime.ParseExact (desc.Substring (4), "yyyyMMddHHmmss", null);
					break;
				case WebRequestMethods.Ftp.MakeDirectory:
					if (status.StatusCode != FtpStatusCode.PathnameCreated)
						throw CreateExceptionFromResponse (status);
					break;
				case ChangeDir:
					method = WebRequestMethods.Ftp.PrintWorkingDirectory;

					if (status.StatusCode != FtpStatusCode.FileActionOK)
						throw CreateExceptionFromResponse (status);

					status = SendCommand (method);

					if (status.StatusCode != FtpStatusCode.PathnameCreated)
						throw CreateExceptionFromResponse (status);
					break;
				case RenameFromCommand:
					method = WebRequestMethods.Ftp.Rename;
					if (status.StatusCode != FtpStatusCode.FileCommandPending)
						throw CreateExceptionFromResponse (status);
					// Pass an empty string if RenameTo wasn't specified
					status = SendCommand (RenameToCommand, renameTo != null ? renameTo : String.Empty);
					if (status.StatusCode != FtpStatusCode.FileActionOK)
						throw CreateExceptionFromResponse (status);
					break;
				case WebRequestMethods.Ftp.DeleteFile:
					if (status.StatusCode != FtpStatusCode.FileActionOK)
						throw CreateExceptionFromResponse (status);
					break;
				}

			State = RequestState.Finished;
			}

		private void UploadData ()
			{
			State = RequestState.OpeningData;

			OpenDataConnection ();

			State = RequestState.TransferInProgress;
			requestStream = new FtpDataStream (this, dataStream, false);
			asyncResult.Stream = requestStream;
			}

		private void DownloadData ()
			{
			State = RequestState.OpeningData;

			OpenDataConnection ();

			State = RequestState.TransferInProgress;
			ftpResponse.Stream = new FtpDataStream (this, dataStream, true);
			}

		private void CheckRequestStarted ()
			{
			if (State != RequestState.Before)
				throw new InvalidOperationException ("There is a request currently in progress");
			}

		private void OpenControlConnection ()
			{
			Exception exception = null;
#if SSHARP
			CrestronClientSocket sock = null;
#else
			Socket sock = null;
#endif
			foreach (IPAddress address in hostEntry.AddressList)
				{
#if SSHARP
#if !IPV6
				if (address.AddressFamily == AddressFamily.InterNetworkV6)
					continue;
#endif

				sock = new CrestronClientSocket ();
#else
				sock = new Socket (address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
#endif

				IPEndPoint remote = new IPEndPoint (address, requestUri.Port);

				if (!ServicePoint.CallEndPointDelegate (sock, remote))
					{
					sock.Close ();
					sock = null;
					}
				else
					{
					try
						{
						sock.Connect (remote);
						localEndPoint = (IPEndPoint)sock.LocalEndPoint;
						break;
						}
					catch (SocketException exc)
						{
						exception = exc;
						sock.Close ();
						sock = null;
						}
					}
				}

			// Couldn't connect to any address
			if (sock == null)
				throw new WebException ("Unable to connect to remote server", exception, WebExceptionStatus.UnknownError, ftpResponse);

			controlStream = new NetworkStream (sock);
			controlReader = new StreamReader (controlStream, Encoding.ASCII);

			State = RequestState.Authenticating;

			Authenticate ();
			FtpStatus status = SendCommand ("OPTS", "utf8", "on");
			// ignore status for OPTS
			status = SendCommand (WebRequestMethods.Ftp.PrintWorkingDirectory);
			initial_path = GetInitialPath (status);
			}

		private static string GetInitialPath (FtpStatus status)
			{
			int s = (int)status.StatusCode;
			if (s < 200 || s > 300 || status.StatusDescription.Length <= 4)
				throw new WebException ("Error getting current directory: " + status.StatusDescription, null, WebExceptionStatus.UnknownError, null);

			string msg = status.StatusDescription.Substring (4);
			if (msg[0] == '"')
				{
				int next_quote = msg.IndexOf ('\"', 1);
				if (next_quote == -1)
					throw new WebException ("Error getting current directory: PWD -> " + status.StatusDescription, null, WebExceptionStatus.UnknownError, null);

				msg = msg.Substring (1, next_quote - 1);
				}

			if (!msg.EndsWith ("/"))
				msg += "/";
			return msg;
			}

		// Probably we could do better having here a regex
		private Socket SetupPassiveConnection (string statusDescription)
			{
			// Current response string
			string response = statusDescription;
			if (response.Length < 4)
				throw new WebException ("Cannot open passive data connection");

			// Look for first digit after code
			int i;
			for (i = 3; i < response.Length && !Char.IsDigit (response[i]); i++)
				;
			if (i >= response.Length)
				throw new WebException ("Cannot open passive data connection");

			// Get six elements
			string[] digits = response.Substring (i).Split (new char[] {','}, 6);
			if (digits.Length != 6)
				throw new WebException ("Cannot open passive data connection");

			// Clean non-digits at the end of last element
			int j;
			for (j = digits[5].Length - 1; j >= 0 && !Char.IsDigit (digits[5][j]); j--)
				;
			if (j < 0)
				throw new WebException ("Cannot open passive data connection");

			digits[5] = digits[5].Substring (0, j + 1);

			IPAddress ip;
			try
				{
				ip = IPAddress.Parse (String.Join (".", digits, 0, 4));
				}
			catch (FormatException)
				{
				throw new WebException ("Cannot open passive data connection");
				}

			// Get the port
			int p1, p2, port;
#if SSHARP
			if (!TryParsers.Int32TryParse (digits[4], out p1) || !TryParsers.Int32TryParse (digits[5], out p2))
#else
			if (!Int32.TryParse (digits[4], out p1) || !Int32.TryParse (digits[5], out p2))
#endif
				throw new WebException ("Cannot open passive data connection");

			port = (p1 << 8) + p2; // p1 * 256 + p2
			//port = p1 * 256 + p2;
			if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
				throw new WebException ("Cannot open passive data connection");

			IPEndPoint ep = new IPEndPoint (ip, port);
#if SSHARP
			CrestronClientSocket sock = new CrestronClientSocket ();
#else
			Socket sock = new Socket (ep.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
#endif
			try
				{
				sock.Connect (ep);
				}
			catch (SocketException)
				{
				sock.Close ();
				throw new WebException ("Cannot open passive data connection");
				}

			return sock;
			}

		private Exception CreateExceptionFromResponse (FtpStatus status)
			{
			FtpWebResponse ftpResponse = new FtpWebResponse (this, requestUri, method, status);

			WebException exc = new WebException ("Server returned an error: " + status.StatusDescription, null, WebExceptionStatus.ProtocolError, ftpResponse);
			return exc;
			}

		// Here we could also get a server error, so be cautious
		internal void SetTransferCompleted ()
			{
			if (InFinalState ())
				return;

			State = RequestState.Finished;
			FtpStatus status = GetResponseStatus ();
			ftpResponse.UpdateStatus (status);
			if (!keepAlive)
				CloseConnection ();
			}

		internal void OperationCompleted ()
			{
			if (!keepAlive)
				CloseConnection ();
			}

		private void SetCompleteWithError (Exception exc)
			{
			if (asyncResult != null)
				asyncResult.SetCompleted (false, exc);
			}

#if SSHARP
		private object InitDataConnection ()
#else
		private Socket InitDataConnection ()
#endif
			{
			FtpStatus status;

			if (usePassive)
				{
				status = SendCommand (PassiveCommand);
				if (status.StatusCode != FtpStatusCode.EnteringPassive)
					throw CreateExceptionFromResponse (status);

				return SetupPassiveConnection (status.StatusDescription);
				}

			// Open a socket to listen the server's connection
#if SSHARP
			CrestronListenerSocket sock = new CrestronListenerSocket (IPAddress.Any, 0);
#else
			Socket sock = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
#endif
			try
				{
				sock.Bind (new IPEndPoint (localEndPoint.Address, 0));
				sock.Listen (1); // We only expect a connection from server
				}
			catch (SocketException e)
				{
				sock.Close ();

				throw new WebException ("Couldn't open listening socket on client", e);
				}

			IPEndPoint ep = (IPEndPoint)sock.LocalEndPoint;
			string ipString = ep.Address.ToString ().Replace ('.', ',');
			int h1 = ep.Port >> 8; // ep.Port / 256
			int h2 = ep.Port % 256;

			string portParam = ipString + "," + h1 + "," + h2;
			status = SendCommand (PortCommand, portParam);

			if (status.StatusCode != FtpStatusCode.CommandOK)
				{
				sock.Close ();
				throw (CreateExceptionFromResponse (status));
				}

			return sock;
			}

		private void OpenDataConnection ()
			{
			FtpStatus status;

#if SSHARP
			object s = InitDataConnection ();
#else
			Socket s = InitDataConnection ();
#endif

			// Handle content offset
			if (offset > 0)
				{
				status = SendCommand (RestCommand, offset.ToString ());
				if (status.StatusCode != FtpStatusCode.FileCommandPending)
					throw CreateExceptionFromResponse (status);
				}

			if (method != WebRequestMethods.Ftp.ListDirectory && method != WebRequestMethods.Ftp.ListDirectoryDetails
			    && method != WebRequestMethods.Ftp.UploadFileWithUniqueName)
				status = SendCommand (method, file_name);
			else
				status = SendCommand (method);

			if (status.StatusCode != FtpStatusCode.OpeningData && status.StatusCode != FtpStatusCode.DataAlreadyOpen)
				throw CreateExceptionFromResponse (status);

			if (usePassive)
				{
#if SSHARP
				origDataStream = new NetworkStream ((CrestronClientSocket)s, true);
#else
				origDataStream = new NetworkStream (s, true);
#endif
				dataStream = origDataStream;
				if (EnableSsl)
					ChangeToSSLSocket (ref dataStream);
				}
			else
				{
				// Active connection (use Socket.Blocking to true)
#if SSHARP
				CrestronServerSocket incoming = null;
				try
					{
					incoming = ((CrestronListenerSocket)s).Accept ();
					}
#else
				Socket incoming = null;
				try
					{
					incoming = s.Accept ();
					}
#endif
				catch (SocketException)
					{
#if SSHARP
					((CrestronListenerSocket)s).Close ();
#else
					s.Close ();
#endif
					if (incoming != null)
						incoming.Close ();

					throw new ProtocolViolationException ("Server commited a protocol violation.");
					}

#if SSHARP
				((CrestronListenerSocket)s).Close ();
#else
				s.Close ();
#endif
				origDataStream = new NetworkStream (incoming, true);
				dataStream = origDataStream;
				if (EnableSsl)
					ChangeToSSLSocket (ref dataStream);
				}

			ftpResponse.UpdateStatus (status);
			}

		private void Authenticate ()
			{
			string username = null;
			string password = null;
			string domain = null;

			if (credentials != null)
				{
				username = credentials.UserName;
				password = credentials.Password;
				domain = credentials.Domain;
				}

			if (username == null)
				username = "anonymous";
			if (password == null)
				password = "@anonymous";
			if (!string.IsNullOrEmpty (domain))
				username = domain + '\\' + username;

			// Connect to server and get banner message
			FtpStatus status = GetResponseStatus ();
			ftpResponse.BannerMessage = status.StatusDescription;

			if (EnableSsl)
				{
				InitiateSecureConnection (ref controlStream);
				controlReader = new StreamReader (controlStream, Encoding.ASCII);
				status = SendCommand ("PBSZ", "0");
				int st = (int)status.StatusCode;
				if (st < 200 || st >= 300)
					throw CreateExceptionFromResponse (status);
				// TODO: what if "PROT P" is denied by the server? What does MS do?
				status = SendCommand ("PROT", "P");
				st = (int)status.StatusCode;
				if (st < 200 || st >= 300)
					throw CreateExceptionFromResponse (status);

				status = new FtpStatus (FtpStatusCode.SendUserCommand, "");
				}

			if (status.StatusCode != FtpStatusCode.SendUserCommand)
				throw CreateExceptionFromResponse (status);

			status = SendCommand (UserCommand, username);

			switch (status.StatusCode)
				{
				case FtpStatusCode.SendPasswordCommand:
					status = SendCommand (PasswordCommand, password);
					if (status.StatusCode != FtpStatusCode.LoggedInProceed)
						throw CreateExceptionFromResponse (status);
					break;
				case FtpStatusCode.LoggedInProceed:
					break;
				default:
					throw CreateExceptionFromResponse (status);
				}

			ftpResponse.WelcomeMessage = status.StatusDescription;
			ftpResponse.UpdateStatus (status);
			}

		private FtpStatus SendCommand (string command, params string[] parameters)
			{
			return SendCommand (true, command, parameters);
			}

		private FtpStatus SendCommand (bool waitResponse, string command, params string[] parameters)
			{
			byte[] cmd;
			string commandString = command;
			if (parameters.Length > 0)
				commandString += " " + String.Join (" ", parameters);

			commandString += EOL;
			cmd = Encoding.ASCII.GetBytes (commandString);
			try
				{
				controlStream.Write (cmd, 0, cmd.Length);
				}
			catch (IOException)
				{
				//controlStream.Close ();
				return new FtpStatus (FtpStatusCode.ServiceNotAvailable, "Write failed");
				}

			if (!waitResponse)
				return null;

			FtpStatus result = GetResponseStatus ();
			if (ftpResponse != null)
				ftpResponse.UpdateStatus (result);
			return result;
			}

		internal static FtpStatus ServiceNotAvailable ()
			{
			return new FtpStatus (FtpStatusCode.ServiceNotAvailable, Locale.GetText ("Invalid response from server"));
			}

		internal FtpStatus GetResponseStatus ()
			{
			while (true)
				{
				string response = null;

				try
					{
					response = controlReader.ReadLine ();
					}
				catch (IOException)
					{
					}

				if (response == null || response.Length < 3)
					return ServiceNotAvailable ();

				int code;
#if SSHARP
				if (!TryParsers.Int32TryParse (response.Substring (0, 3), out code))
#else
				if (!Int32.TryParse (response.Substring (0, 3), out code))
#endif
					return ServiceNotAvailable ();

				if (response.Length > 3 && response[3] == '-')
					{
					string line = null;
					string find = code.ToString () + ' ';
					while (true)
						{
						line = null;
						try
							{
							line = controlReader.ReadLine ();
							}
						catch (IOException)
							{
							}
						if (line == null)
							return ServiceNotAvailable ();

						response += Environment.NewLine + line;

						if (line.StartsWith (find, StringComparison.Ordinal))
							break;
						}
					}
				return new FtpStatus ((FtpStatusCode)code, response);
				}
			}

		private void InitiateSecureConnection (ref Stream stream)
			{
			FtpStatus status = SendCommand (AuthCommand, "TLS");
			if (status.StatusCode != FtpStatusCode.ServerWantsSecureSession)
				throw CreateExceptionFromResponse (status);

			ChangeToSSLSocket (ref stream);
			}

#if SECURITY_DEP
		private RemoteCertificateValidationCallback callback = delegate (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
			{
			// honor any exciting callback defined on ServicePointManager
			if (ServicePointManager.ServerCertificateValidationCallback != null)
				return ServicePointManager.ServerCertificateValidationCallback (sender, certificate, chain, sslPolicyErrors);
			// otherwise provide our own
			if (sslPolicyErrors != SslPolicyErrors.None)
				throw new InvalidOperationException ("SSL authentication error: " + sslPolicyErrors);
			return true;
			};
#endif

		internal bool ChangeToSSLSocket (ref Stream stream)
			{
#if   SECURITY_DEP
			SslStream sslStream = new SslStream (stream, true, callback, null);
			//sslStream.AuthenticateAsClient (Host, this.ClientCertificates, SslProtocols.Default, false);
			//TODO: client certificates
			sslStream.AuthenticateAsClient (requestUri.Host, null, SslProtocols.Default, false);
			stream = sslStream;
			return true;
#else
			throw new NotImplementedException ();
#endif
			}

		private bool InFinalState ()
			{
			return (State == RequestState.Aborted || State == RequestState.Error || State == RequestState.Finished);
			}

		private bool InProgress ()
			{
			return (State != RequestState.Before && !InFinalState ());
			}

		internal void CheckIfAborted ()
			{
			if (State == RequestState.Aborted)
				throw new WebException ("Request aborted", WebExceptionStatus.RequestCanceled);
			}

		private void CheckFinalState ()
			{
			if (InFinalState ())
				throw new InvalidOperationException ("Cannot change final state");
			}
		}
	}