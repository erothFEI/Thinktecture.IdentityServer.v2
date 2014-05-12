﻿#region License Header
// /*******************************************************************************
//  * Open Behavioral Health Information Technology Architecture (OBHITA.org)
//  * 
//  * Redistribution and use in source and binary forms, with or without
//  * modification, are permitted provided that the following conditions are met:
//  *     * Redistributions of source code must retain the above copyright
//  *       notice, this list of conditions and the following disclaimer.
//  *     * Redistributions in binary form must reproduce the above copyright
//  *       notice, this list of conditions and the following disclaimer in the
//  *       documentation and/or other materials provided with the distribution.
//  *     * Neither the name of the <organization> nor the
//  *       names of its contributors may be used to endorse or promote products
//  *       derived from this software without specific prior written permission.
//  * 
//  * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//  * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//  * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//  * DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> BE LIABLE FOR ANY
//  * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//  * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//  * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//  * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//  * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//  * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//  ******************************************************************************/
#endregion
namespace Thinktecture.IdentityServer.Web.Controller.Api
{
    #region

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Mail;
    using System.Text;
    using System.Web;
    using System.Web.Configuration;
    using System.Web.Http;
    using System.Web.Security;
    using IdentityModel.Authorization.WebApi;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;

    #endregion

    [ClaimsAuthorize(Constants.Actions.WebApi, Constants.Resources.General)] 
    public class MembershipController : ApiController
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        public HttpResponseMessage Get(string username)
        {
            try
            {
                var user = Membership.GetUser(username);
                if (user == null)
                {
                    var httpError = new HttpError(string.Format("The username '{0}' does not exist.", username));
                    _logger.Debug(httpError.Message);
                    httpError["error_sub_code"] = 1003;
                    return Request.CreateErrorResponse(HttpStatusCode.NotFound, httpError);
                }
                return Request.CreateResponse(HttpStatusCode.OK, Map(user));
            }
            catch (Exception ex)
            {
                var message = string.Format("Cannot retrieve user by username '{0}'.", username);
                _logger.DebugException(message, ex);
                var httpError = new HttpError(message);
                httpError["error_sub_code"] = 1010;
                httpError["error"] = ex.Message;
                return Request.CreateErrorResponse(HttpStatusCode.Conflict, httpError);
            }
        }

        public HttpResponseMessage GetUserByEmail(string email)
        {
            try
            {
                var users = Membership.FindUsersByEmail(email);
                return Request.CreateResponse(HttpStatusCode.OK, Map(users));
            }
            catch (Exception ex)
            {
                var message = string.Format("Cannot retrieve user by email '{0}'.", email);
                _logger.DebugException(message, ex);
                var httpError = new HttpError(message);
                httpError["error_sub_code"] = 1009;
                httpError["error"] = ex.Message;
                return Request.CreateErrorResponse(HttpStatusCode.Conflict, httpError);
            }
        }

        [AcceptVerbs("PUT")]
        public HttpResponseMessage Create(string username)
        {
            // Error handling : http://www.asp.net/web-api/overview/web-api-routing-and-actions/exception-handling
            // Web Api return HttpResponseMessage http://stackoverflow.com/questions/12264088/asp-net-web-api-return-clr-object-or-httpresponsemessage
            MembershipUser user;
            try
            {
                user = Membership.GetUser(username);
                if (user != null)
                {
                    var message = string.Format("The username '{0}' is already in use.", username);
                    var httpError = new HttpError(message);
                    _logger.Debug(httpError.Message);
                    httpError["error_sub_code"] = 1001; //can add custom Key-Values to HttpError
                    return Request.CreateErrorResponse(HttpStatusCode.Conflict, httpError);
                }
            }
            catch (Exception ex)
            {
                var message = string.Format("Cannot retrieve user by username '{0}'.", username);
                _logger.DebugException(message, ex);
                var httpError = new HttpError(message);
                httpError["error_sub_code"] = 1010;
                httpError["error"] = ex.Message;
                return Request.CreateErrorResponse(HttpStatusCode.Conflict, httpError);
            }

            string password;
            try
            {
                password = Membership.GeneratePassword(10, 3);
                user = Membership.CreateUser(username, password, username);
            }
            catch (Exception ex)
            {
                var message = string.Format("Cannot create user '{0}'.", username);
                _logger.DebugException(message, ex);
                var httpError = new HttpError(message);
                httpError["error_sub_code"] = 1005;
                httpError["error"] = ex.Message;
                return Request.CreateErrorResponse(HttpStatusCode.Conflict, httpError);
            }

            try
            {
                SetRolesForUser(username, new[] { Constants.Roles.IdentityServerUsers });
            }
            catch (Exception ex)
            {
                var message = string.Format("Cannot set role for user '{0}'.", username);
                _logger.DebugException(message, ex);
                var httpError = new HttpError(message);
                httpError["error_sub_code"] = 1007;
                httpError["error"] = ex.Message;
                return Request.CreateErrorResponse(HttpStatusCode.Conflict, httpError);
            }

            try
            {
                SendEmailNotification(user, password);
                return Request.CreateResponse(HttpStatusCode.OK, Map(user));
            }
            catch (Exception ex)
            {
                var message = string.Format("Cannot send email out for '{0}'.", username);
                _logger.DebugException(message, ex);
                var httpError = new HttpError(message);
                httpError["error_sub_code"] = 1006;
                httpError["error"] = ex.Message;
                return Request.CreateErrorResponse(HttpStatusCode.Conflict, httpError);
            }
        }

        [AcceptVerbs("POST")]
        public HttpResponseMessage Unlock(string username)
        {
            var user = Membership.GetUser(username);
            if (user == null)
            {
                var httpError = new HttpError(string.Format("The username '{0}' does not exist.", username));
                _logger.Debug(httpError.Message);
                httpError["error_sub_code"] = 1003;
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, httpError);
            }
            if ( !user.IsApproved )
            {
                user.IsApproved = true;
            }
            if (user.IsLockedOut)
            {
                user.UnlockUser();
            }
            else
            {
                Membership.UpdateUser ( user );
            }
            return Request.CreateResponse(HttpStatusCode.OK, Map(user));
        }

        [AcceptVerbs("POST")]
        public HttpResponseMessage Lock(string username)
        {
            var user = Membership.GetUser(username);
            if (user == null)
            {
                var httpError = new HttpError(string.Format("The username '{0}' does not exist.", username));
                _logger.Debug(httpError.Message);
                httpError["error_sub_code"] = 1003;
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, httpError);
            }
            if (user.IsApproved)
            {
                user.IsApproved = false;
                Membership.UpdateUser(user);
            }
            return Request.CreateResponse(HttpStatusCode.OK, Map(user));
        }

        [AcceptVerbs("POST")]
        public HttpResponseMessage ChangePassword(string username)
        {
            var json = JsonConvert.DeserializeObject<JObject> ( Request.Content.ReadAsStringAsync ().Result );
            var oldPassword = json["oldPassword"].Value<string>();
            var newPassword = json["newPassword"].Value<string>();
            var validUser = Membership.ValidateUser(username, oldPassword);
            if (!validUser)
            {
                var httpError = new HttpError("Invalid username/password.");
                _logger.Debug(httpError.Message);
                httpError["error_sub_code"] = 1002;
                return Request.CreateErrorResponse(HttpStatusCode.Unauthorized, httpError);
            }
            try
            {
                var user = Membership.GetUser(username);
                user.ChangePassword(oldPassword, newPassword);
                return Request.CreateResponse(HttpStatusCode.OK, Map(user));
            }
            catch (Exception ex)
            {
                var message = string.Format("Cannot change password for user '{0}'.", username);
                _logger.DebugException(message, ex);
                var httpError = new HttpError(message);
                httpError["error_sub_code"] = 1008;
                httpError["error"] = ex.Message;
                return Request.CreateErrorResponse(HttpStatusCode.Conflict, httpError);
            }
        }

        [AcceptVerbs("POST")]
        public HttpResponseMessage ResetPassword(string username)
        {
            try
            {
                var user = Membership.GetUser(username);

                var newPassword = user.ResetPassword ();

                try
                {
                    SendEmailNotification(user, newPassword);
                    return Request.CreateResponse(HttpStatusCode.OK, Map(user));
                }
                catch (Exception ex)
                {
                    var message = string.Format((string) "Cannot send email out for '{0}'.", (object) user.UserName);
                    _logger.DebugException(message, ex);
                    var httpError = new HttpError(message);
                    httpError["error_sub_code"] = 1006;
                    httpError["error"] = ex.Message;
                    return Request.CreateErrorResponse(HttpStatusCode.Conflict, httpError);
                }
            }
            catch (Exception ex)
            {
                var message = string.Format("Cannot reset password for user '{0}'.", username);
                _logger.DebugException(message, ex);
                var httpError = new HttpError(message);
                httpError["error_sub_code"] = 1008;
                httpError["error"] = ex.Message;
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, httpError);
            }
        }

        public static void SendEmailNotification(MembershipUser user, string password)
        {
            var fullname = user.UserName;
            var emailTemplate = File.ReadAllText ( HttpContext.Current.Server.MapPath("/EmailTemplate.html") );
            var body = string.Format(emailTemplate, fullname, user.UserName.ToLower(), password);
            using (var message = new MailMessage
                {
                    Subject = WebConfigurationManager.AppSettings["EmailWelcomeSubject"],
                    Body = body,
                    BodyEncoding = Encoding.UTF8,
                    IsBodyHtml = true,
                })
            {
                message.To.Add(new MailAddress(user.Email));
                var cc = WebConfigurationManager.AppSettings["EmailCC"];
                if (!string.IsNullOrWhiteSpace(cc))
                {
                    message.CC.Add(new MailAddress(cc));
                }

                using ( var smtp = new SmtpClient() )
                {
                    ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
                    smtp.Send(message);
                }
            }
        }

        private static void SetRolesForUser(string userName, IEnumerable<string> roles)
        {
            var userRoles = Roles.GetRolesForUser(userName);

            if (userRoles.Length != 0)
            {
                Roles.RemoveUserFromRoles(userName, userRoles);
            }

            if (roles.Any())
            {
                Roles.AddUserToRoles(userName, roles.ToArray());
            }
        }

        private static MembershipUserDto Map(MembershipUser user)
        {
            return new MembershipUserDto
                {
                    Username = user.UserName,
                    NameIdentifier = user.Email,
                    Email = user.Email,
                    IsApproved = user.IsApproved,
                    IsLockedOut = user.IsLockedOut,
                    LastLockoutDate = user.LastLockoutDate
                };
        }

        private static IEnumerable<MembershipUserDto> Map(MembershipUserCollection users)
        {
            return users.Cast<MembershipUser>().Select(Map).ToList();
        }
    }
}