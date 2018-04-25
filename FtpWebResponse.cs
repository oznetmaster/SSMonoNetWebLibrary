//
// System.Net.FtpWebResponse.cs
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
#else
using System.IO;
using System.Runtime.Serialization;
using System.Net;
#endif

#if SSHARP
namespace SSMono.Net
#else
namespace System.Net
#endif
	{
	public class FtpWebResponse : WebResponse
		{
		private Stream stream;
		private Uri uri;
		private FtpStatusCode statusCode;
		private DateTime lastModified = DateTime.MinValue;
		private string bannerMessage = String.Empty;
		private string welcomeMessage = String.Empty;
		private string exitMessage = String.Empty;
		private string statusDescription;
		private string method;
		//bool keepAlive;
		private bool disposed;
		private FtpWebRequest request;
		internal long contentLength = -1;

		internal FtpWebResponse (FtpWebRequest request, Uri uri, string method, bool keepAlive)
			{
			this.request = request;
			this.uri = uri;
			this.method = method;
			//this.keepAlive = keepAlive;
			}

		internal FtpWebResponse (FtpWebRequest request, Uri uri, string method, FtpStatusCode statusCode, string statusDescription)
			{
			this.request = request;
			this.uri = uri;
			this.method = method;
			this.statusCode = statusCode;
			this.statusDescription = statusDescription;
			}

		internal FtpWebResponse (FtpWebRequest request, Uri uri, string method, FtpStatus status)
			: this (request, uri, method, status.StatusCode, status.StatusDescription)
			{
			}

		public override long ContentLength
			{
			get { return contentLength; }
			}

		public override WebHeaderCollection Headers
			{
			get { return new WebHeaderCollection (); }
			}

		public override Uri ResponseUri
			{
			get { return uri; }
			}

		public DateTime LastModified
			{
			get { return lastModified; }
			internal set { lastModified = value; }
			}

		public string BannerMessage
			{
			get { return bannerMessage; }
			internal set { bannerMessage = value; }
			}

		public string WelcomeMessage
			{
			get { return welcomeMessage; }
			internal set { welcomeMessage = value; }
			}

		public string ExitMessage
			{
			get { return exitMessage; }
			internal set { exitMessage = value; }
			}

		public FtpStatusCode StatusCode
			{
			get { return statusCode; }
			internal set { statusCode = value; }
			}

		public string StatusDescription
			{
			get { return statusDescription; }
			internal set { statusDescription = value; }
			}

		public override void Close ()
			{
			if (disposed)
				return;

			disposed = true;
			if (stream != null)
				{
				stream.Close ();
				if (stream == Stream.Null)
					request.OperationCompleted ();
				}
			stream = null;
			}

		public override Stream GetResponseStream ()
			{
			if (stream == null)
				return Stream.Null; // After a STOR we get this

			if (method != WebRequestMethods.Ftp.DownloadFile && method != WebRequestMethods.Ftp.ListDirectory)
				CheckDisposed ();

			return stream;
			}

		internal Stream Stream
			{
			set { stream = value; }

			get { return stream; }
			}

		internal void UpdateStatus (FtpStatus status)
			{
			statusCode = status.StatusCode;
			statusDescription = status.StatusDescription;
			}

		private void CheckDisposed ()
			{
			if (disposed)
				throw new ObjectDisposedException (GetType ().FullName);
			}

		internal bool IsFinal ()
			{
			return ((int)statusCode >= 200);
			}
		}
	}