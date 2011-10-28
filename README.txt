ELMAH fix for .NET 4.0 Request Validation Exceptions

.NET 4.0 request validation is more protective than in .NET 2.0.
It fires whenever you try to access the Request.QueryString or Request.Form properties.

Elmah always includes the QueryString and Form data in every exception, meaning that this validation fires for every
exception logged in Elmah.  The problem lies when the QueryString or Form data contains potentially dangerous content.
When this occurs, Elmah itself raises this exception and fails to log any error (or send an email, for example).

This defect is logged here: http://code.google.com/p/elmah/issues/detail?id=217&colspec=ID%20Type%20Status%20Priority%20Stars%20Milestone%20Owner%20Summary

Elmah targets the .NET 3.5 Framework, and therefore does not have access to "unvalidated" versions of these properties.  

For this reason, I've modified the Error.cs class to catch when these exceptions occur, and continue to log 
the exception without the QueryString or Form details in the exception context.

Other more involved ways of solving this problem include:

* Updating Elmah to target the .NET 4.0 framework, and using the Request UnvalidatedRequestValues to obtain the QueryString and Form data.
The problem with this approach is the large user base that may rely on the fact that Elmah targets the .NET 3.5 Framework.

* Allow the use of HttpContextBase when raising an ErrorSignal, instead of the sealed HttpContext class.  This way, .NET 4.0 users could
simply inject a HttpContextWrapper which overrides the Request property and returns the UnvalidatedRequestValues for QueryString and Form.
The problem with this approach is that it will require extensive changes to the Elmah public API, replacing (or adding overloads) for
HttpContextBase where HttpContext is currently the only option.

For these reasons, I've implemented a satisfactory solution for my needs, which is to catch these exceptions when they occur, and
continue to log the exception as normal, just without the QueryString and Form data.
