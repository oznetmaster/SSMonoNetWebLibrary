//
// System.Net.HttpWebResponse
//
// Authors:
// 	Lawrence Pit (loz@cable.a2000.nl)
// 	Gonzalo Paniagua Javier (gonzalo@ximian.com)
//      Daniel Nauck    (dna(at)mono-project(dot)de)
//
// (c) 2002 Lawrence Pit
// (c) 2003 Ximian, Inc. (http://www.ximian.com)
// (c) 2008 Daniel Nauck
//

// Copyright (c) 2018 Nivloc Enterprises Ltd

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.Globalization;
#if SSHARP
using Crestron.SimplSharp.CrestronIO;
using Crestron.SimplSharp.CrestronIO.Compression;
#else
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Runtime.Serialization;
#endif
using System.Text;

#if SSHARP
namespace SSMono.Net
#else
namespace System.Net
#endif
	{
	[Serializable]
	public class HttpWebResponse : WebResponse,
#if !NETCF
		ISerializable,
#endif
		IDisposable
		{
		private Uri uri;
		private WebHeaderCollection webHeaders;
		private CookieCollection cookieCollection;
		private string method;
		private Version version;
		private HttpStatusCode statusCode;
		private string statusDescription;
		private long contentLength;
		private string contentType;
		private CookieContainer cookie_container;

		private bool disposed;
		private Stream stream;

		// Constructors

		internal HttpWebResponse (Uri uri, string method, WebConnectionData data, CookieContainer container)
			{
			this.uri = uri;
			this.method = method;
			webHeaders = data.Headers;
			version = data.Version;
			statusCode = (HttpStatusCode)data.StatusCode;
			statusDescription = data.StatusDescription;
			stream = data.stream;
			contentLength = -1;

			try
				{
				string cl = webHeaders["Content-Length"];
#if SSHARP
				if (String.IsNullOrEmpty (cl) || !TryParsers.Int64TryParse (cl, out contentLength))
#else
				if (String.IsNullOrEmpty (cl) || !Int64.TryParse (cl, out contentLength))
#endif
					contentLength = -1;
				}
			catch (Exception)
				{
				contentLength = -1;
				}

			if (container != null)
				{
				this.cookie_container = container;
				FillCookies ();
				}

			string content_encoding = webHeaders["Content-Encoding"];
			if (content_encoding == "gzip" && (data.request.AutomaticDecompression & DecompressionMethods.GZip) != 0)
				stream = new GZipStream (stream, CompressionMode.Decompress);
			else if (content_encoding == "deflate" && (data.request.AutomaticDecompression & DecompressionMethods.Deflate) != 0)
				stream = new DeflateStream (stream, CompressionMode.Decompress);
			}

#if !NETCF
		[Obsolete ("Serialization is obsoleted for this type", false)]
		protected HttpWebResponse (SerializationInfo serializationInfo, StreamingContext streamingContext)
			{
			SerializationInfo info = serializationInfo;

			uri = (Uri)info.GetValue ("uri", typeof(Uri));
			contentLength = info.GetInt64 ("contentLength");
			contentType = info.GetString ("contentType");
			method = info.GetString ("method");
			statusDescription = info.GetString ("statusDescription");
			cookieCollection = (CookieCollection)info.GetValue ("cookieCollection", typeof(CookieCollection));
			version = (Version)info.GetValue ("version", typeof(Version));
			statusCode = (HttpStatusCode)info.GetValue ("statusCode", typeof(HttpStatusCode));
			}
#endif
		// Properties

		public string CharacterSet
			{
			// Content-Type   = "Content-Type" ":" media-type
			// media-type     = type "/" subtype *( ";" parameter )
			// parameter      = attribute "=" value
			// 3.7.1. default is ISO-8859-1
			get
				{
				string contentType = ContentType;
				if (contentType == null)
					return "ISO-8859-1";
				string val = contentType.ToLower ();
				int pos = val.IndexOf ("charset=", StringComparison.Ordinal);
				if (pos == -1)
					return "ISO-8859-1";
				pos += 8;
				int pos2 = val.IndexOf (';', pos);
				return (pos2 == -1) ? contentType.Substring (pos) : contentType.Substring (pos, pos2 - pos);
				}
			}

		public string ContentEncoding
			{
			get
				{
				CheckDisposed ();
				string h = webHeaders["Content-Encoding"];
				return h != null ? h : "";
				}
			}

		public override long ContentLength
			{
			get { return contentLength; }
			}

		public override string ContentType
			{
			get
				{
				CheckDisposed ();

				if (contentType == null)
					contentType = webHeaders["Content-Type"];

				return contentType;
				}
			}

#if NET_4_5
		virtual
#endif

		public CookieCollection Cookies
			{
			get
				{
				CheckDisposed ();
				if (cookieCollection == null)
					cookieCollection = new CookieCollection ();
				return cookieCollection;
				}
			set
				{
				CheckDisposed ();
				cookieCollection = value;
				}
			}

		public override WebHeaderCollection Headers
			{
			get { return webHeaders; }
			}

		private static Exception GetMustImplement ()
			{
			return new NotImplementedException ();
			}

		[MonoTODO]
		public override bool IsMutuallyAuthenticated
			{
			get { throw GetMustImplement (); }
			}

		public DateTime LastModified
			{
			get
				{
				CheckDisposed ();
				try
					{
					string dtStr = webHeaders["Last-Modified"];
					return MonoHttpDate.Parse (dtStr);
					}
				catch (Exception)
					{
					return DateTime.Now;
					}
				}
			}

#if NET_4_5
		virtual
#endif

		public string Method
			{
			get
				{
				CheckDisposed ();
				return method;
				}
			}

		public Version ProtocolVersion
			{
			get
				{
				CheckDisposed ();
				return version;
				}
			}

		public override Uri ResponseUri
			{
			get
				{
				CheckDisposed ();
				return uri;
				}
			}

		public string Server
			{
			get
				{
				CheckDisposed ();
				return webHeaders["Server"];
				}
			}

#if NET_4_5
		virtual
#endif

		public HttpStatusCode StatusCode
			{
			get { return statusCode; }
			}

#if NET_4_5
		virtual
#endif

		public string StatusDescription
			{
			get
				{
				CheckDisposed ();
				return statusDescription;
				}
			}

		// Methods

		public string GetResponseHeader (string headerName)
			{
			CheckDisposed ();
			string value = webHeaders[headerName];
			return (value != null) ? value : "";
			}

		internal void ReadAll ()
			{
			WebConnectionStream wce = stream as WebConnectionStream;
			if (wce == null)
				return;

			try
				{
				wce.ReadAll ();
				}
			catch
				{
				}
			}

		public override Stream GetResponseStream ()
			{
			CheckDisposed ();
			if (stream == null)
				return Stream.Null;
			if (string.Equals (method, "HEAD", StringComparison.OrdinalIgnoreCase)) // see par 4.3 & 9.4
				return Stream.Null;

			return stream;
			}

#if !NETCF
		void ISerializable.GetObjectData (SerializationInfo serializationInfo, StreamingContext streamingContext)
			{
			GetObjectData (serializationInfo, streamingContext);
			}

		protected override void GetObjectData (SerializationInfo serializationInfo, StreamingContext streamingContext)
			{
			SerializationInfo info = serializationInfo;

			info.AddValue ("uri", uri);
			info.AddValue ("contentLength", contentLength);
			info.AddValue ("contentType", contentType);
			info.AddValue ("method", method);
			info.AddValue ("statusDescription", statusDescription);
			info.AddValue ("cookieCollection", cookieCollection);
			info.AddValue ("version", version);
			info.AddValue ("statusCode", statusCode);
			}
#endif
		// Cleaning up stuff

		public override void Close ()
			{
			if (stream != null)
				{
				Stream st = stream;
				stream = null;
				if (st != null)
					st.Close ();
				}
			}

		void IDisposable.Dispose ()
			{
			Dispose (true);
			}

#if NET_4_0
		protected override void Dispose (bool disposing)
		{
			this.disposed = true;
			base.Dispose (true);
		}
#else
		private void Dispose (bool disposing)
			{
			this.disposed = true;
			if (disposing)
				Close ();
			}
#endif

		private void CheckDisposed ()
			{
			if (disposed)
				throw new ObjectDisposedException (GetType ().FullName);
			}

		private void FillCookies ()
			{
			if (webHeaders == null)
				return;

			//
			// Don't terminate response reading on bad cookie value
			//
			string value;
			try
				{
				value = webHeaders.Get ("Set-Cookie");
				if (value != null && SetCookie (value))
					return;
				}
			catch
				{
				}

			try
				{
				value = webHeaders.Get ("Set-Cookie2");
				if (value != null)
					SetCookie (value);
				}
			catch
				{
				}
			}

		private bool SetCookie (string header)
			{
			if (cookieCollection == null)
				cookieCollection = new CookieCollection ();

			bool at_least_one_set = false;
			var parser = new CookieParser (header);
			foreach (var cookie in parser.Parse ())
				{
				if (cookie.Domain == "")
					{
					cookie.Domain = uri.Host;
					cookie.HasDomain = false;
					}

				if (cookie.HasDomain && !CookieContainer.CheckSameOrigin (uri, cookie.Domain))
					continue;

				cookieCollection.Add (cookie);
				if (cookie_container != null)
					{
					cookie_container.Add (uri, cookie);
					at_least_one_set = true;
					}
				}

			return at_least_one_set;
			}
		}
	}