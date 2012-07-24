#region License, Terms and Author(s)
//
// ELMAH - Error Logging Modules and Handlers for ASP.NET
// Copyright (c) 2004-9 Atif Aziz. All rights reserved.
//
//  Author(s):
//
//      Atif Aziz, http://www.raboof.com
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Modified by David Duffett
// It now catches HttpRequestValidationExceptions thrown by .NET 4.0
// when trying to access the Request.QueryString and Request.Form properties.
// These exceptions are logged without this data, but at least do not
// cause logging to fail.
//
#endregion

using System.Diagnostics;
using System.Text;

[assembly: Elmah.Scc("$Id: Error.cs 776 2011-01-12 21:09:24Z azizatif $")]

namespace Elmah
{
    #region Imports

    using System;
    using System.Security.Principal;
    using System.Web;
    using System.Xml;
    using Thread = System.Threading.Thread;
    using NameValueCollection = System.Collections.Specialized.NameValueCollection;

    #endregion

    /// <summary>
    /// Represents a logical application error (as opposed to the actual 
    /// exception it may be representing).
    /// </summary>

    [ Serializable ]
    public sealed class Error : ICloneable
    {
        private readonly Exception _exception;
        private string _applicationName;
        private string _hostName;
        private string _typeName;
        private string _source;
        private string _message;
        private string _detail;
        private string _user;
        private DateTime _time;
        private int _statusCode;
        private string _webHostHtmlMessage;
        private NameValueCollection _serverVariables;
        private NameValueCollection _queryString;
        private NameValueCollection _form;
        private NameValueCollection _cookies;

        /// <summary>
        /// Initializes a new instance of the <see cref="Error"/> class.
        /// </summary>

        public Error() {}

        /// <summary>
        /// Initializes a new instance of the <see cref="Error"/> class
        /// from a given <see cref="Exception"/> instance.
        /// </summary>

        public Error(Exception e) : 
            this(e, null) {}

        /// <summary>
        /// Initializes a new instance of the <see cref="Error"/> class
        /// from a given <see cref="Exception"/> instance and 
        /// <see cref="HttpContext"/> instance representing the HTTP 
        /// context during the exception.
        /// </summary>

        public Error(Exception e, HttpContext context)
        {
            if (e == null)
                throw new ArgumentNullException("e");

            _exception = e;
            Exception baseException = e.GetBaseException();

            //
            // Load the basic information.
            //

            _hostName = Environment.TryGetMachineName(context);
            _typeName = baseException.GetType().FullName;
            _message = baseException.Message;
            _source = baseException.Source;
            _detail = getExceptionDetail(e, baseException);
            _user = Thread.CurrentPrincipal.Identity.Name ?? string.Empty;
            _time = DateTime.Now;

            //
            // If this is an HTTP exception, then get the status code
            // and detailed HTML message provided by the host.
            //

            HttpException httpException = e as HttpException;

            if (httpException != null)
            {
                _statusCode = httpException.GetHttpCode();
                _webHostHtmlMessage = httpException.GetHtmlErrorMessage() ?? string.Empty;
            }

            //
            // If the HTTP context is available, then capture the
            // collections that represent the state request as well as
            // the user.
            //

            if (context != null)
            {
                IPrincipal webUser = context.User;
                if (webUser != null 
                    && (webUser.Identity.Name ?? string.Empty).Length > 0)
                {
                    _user = webUser.Identity.Name;
                }

                HttpRequest request = context.Request;

                _serverVariables = CopyCollection(request.ServerVariables);
                _cookies = CopyCollection(request.Cookies);

                try
                {
                    _queryString = CopyCollection(request.QueryString);
                    _form = CopyCollection(request.Form);
                }
                catch (HttpRequestValidationException requestValidationException)
                {
                    //
                    // .NET 4.0 will raise this exception if dangerous content is
                    // detected in the request QueryString or Form collections.
                    // In these cases, we will continue to log the exception without
                    // the QueryString or Form data.  We cannot get to this data without
                    // targeting the .NET 4.0 framework and accessing the UnvalidatedRequestValues.
                    //

                    Trace.WriteLine(requestValidationException);
                }
            }
        }

        /// <summary>
        /// Coalesce the exception ToString() methods. Some specific circumstances (AutoMapperMappingException 
        /// coupled with NHibernate lazy load collection) can cause ToString() on exception to fail.
        /// We then attempt to report on the base exception, or as much as we possibly can.
        /// </summary>
        string getExceptionDetail(params Exception[] exceptionsToCoalesce)
        {
            StringBuilder detail = new StringBuilder();
            foreach(var exception in exceptionsToCoalesce)
                try
                {
                    detail.Append(exception.ToString());
                    break;
                }
                catch (Exception loggingException)
                {
                    detail.AppendFormat(
                        "Error logging outer exception details for type '{0}':\r\n" +
                        "  {1}\r\n" +
                        "  \r\n", exception.GetType().Name, loggingException);
                }
            return detail.ToString();
        }

        /// <summary>
        /// Gets the <see cref="Exception"/> instance used to initialize this
        /// instance.
        /// </summary>
        /// <remarks>
        /// This is a run-time property only that is not written or read 
        /// during XML serialization via <see cref="ErrorXml.Decode"/> and 
        /// <see cref="ErrorXml.Encode(Error,XmlWriter)"/>.
        /// </remarks>

        public Exception Exception
        {
            get { return _exception; }
        }

        /// <summary>
        /// Gets or sets the name of application in which this error occurred.
        /// </summary>

        public string ApplicationName
        { 
            get { return _applicationName ?? string.Empty; }
            set { _applicationName = value; }
        }

        /// <summary>
        /// Gets or sets name of host machine where this error occurred.
        /// </summary>
        
        public string HostName
        { 
            get { return _hostName ?? string.Empty; }
            set { _hostName = value; }
        }

        /// <summary>
        /// Gets or sets the type, class or category of the error.
        /// </summary>
        
        public string Type
        { 
            get { return _typeName ?? string.Empty; }
            set { _typeName = value; }
        }

        /// <summary>
        /// Gets or sets the source that is the cause of the error.
        /// </summary>
        
        public string Source
        { 
            get { return _source ?? string.Empty; }
            set { _source = value; }
        }

        /// <summary>
        /// Gets or sets a brief text describing the error.
        /// </summary>
        
        public string Message 
        { 
            get { return _message ?? string.Empty; }
            set { _message = value; }
        }

        /// <summary>
        /// Gets or sets a detailed text describing the error, such as a
        /// stack trace.
        /// </summary>

        public string Detail
        { 
            get { return _detail ?? string.Empty; }
            set { _detail = value; }
        }

        /// <summary>
        /// Gets or sets the user logged into the application at the time 
        /// of the error.
        /// </summary>
        
        public string User 
        { 
            get { return _user ?? string.Empty; }
            set { _user = value; }
        }

        /// <summary>
        /// Gets or sets the date and time (in local time) at which the 
        /// error occurred.
        /// </summary>
        
        public DateTime Time 
        { 
            get { return _time; }
            set { _time = value; }
        }

        /// <summary>
        /// Gets or sets the HTTP status code of the output returned to the 
        /// client for the error.
        /// </summary>
        /// <remarks>
        /// For cases where this value cannot always be reliably determined, 
        /// the value may be reported as zero.
        /// </remarks>
        
        public int StatusCode 
        { 
            get { return _statusCode; }
            set { _statusCode = value; }
        }

        /// <summary>
        /// Gets or sets the HTML message generated by the web host (ASP.NET) 
        /// for the given error.
        /// </summary>
        
        public string WebHostHtmlMessage
        {
            get { return _webHostHtmlMessage ?? string.Empty; }
            set { _webHostHtmlMessage = value; }
        }

        /// <summary>
        /// Gets a collection representing the Web server variables
        /// captured as part of diagnostic data for the error.
        /// </summary>
        
        public NameValueCollection ServerVariables 
        { 
            get { return FaultIn(ref _serverVariables);  }
        }

        /// <summary>
        /// Gets a collection representing the Web query string variables
        /// captured as part of diagnostic data for the error.
        /// </summary>
        
        public NameValueCollection QueryString 
        { 
            get { return FaultIn(ref _queryString); } 
        }

        /// <summary>
        /// Gets a collection representing the form variables captured as 
        /// part of diagnostic data for the error.
        /// </summary>
        
        public NameValueCollection Form 
        { 
            get { return FaultIn(ref _form); }
        }

        /// <summary>
        /// Gets a collection representing the client cookies
        /// captured as part of diagnostic data for the error.
        /// </summary>

        public NameValueCollection Cookies 
        {
            get { return FaultIn(ref _cookies); }
        }

        /// <summary>
        /// Returns the value of the <see cref="Message"/> property.
        /// </summary>

        public override string ToString()
        {
            return this.Message;
        }

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>

        object ICloneable.Clone()
        {
            //
            // Make a base shallow copy of all the members.
            //

            Error copy = (Error) MemberwiseClone();

            //
            // Now make a deep copy of items that are mutable.
            //

            copy._serverVariables = CopyCollection(_serverVariables);
            copy._queryString = CopyCollection(_queryString);
            copy._form = CopyCollection(_form);
            copy._cookies = CopyCollection(_cookies);

            return copy;
        }

        private static NameValueCollection CopyCollection(NameValueCollection collection)
        {
            if (collection == null || collection.Count == 0)
                return null;

            return new NameValueCollection(collection);
        }

        private static NameValueCollection CopyCollection(HttpCookieCollection cookies)
        {
            if (cookies == null || cookies.Count == 0)
                return null;

            NameValueCollection copy = new NameValueCollection(cookies.Count);

            for (int i = 0; i < cookies.Count; i++)
            {
                HttpCookie cookie = cookies[i];

                //
                // NOTE: We drop the Path and Domain properties of the 
                // cookie for sake of simplicity.
                //

                copy.Add(cookie.Name, cookie.Value);
            }

            return copy;
        }

        private static NameValueCollection FaultIn(ref NameValueCollection collection)
        {
            if (collection == null)
                collection = new NameValueCollection();

            return collection;
        }
    }
}
