﻿using System;
using System.Configuration;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using SendGrid;
using SendGrid.Helpers.Mail;
using TFGv1_1.Models;


namespace TFGv1_1
{
    public class EmailService : IIdentityMessageService
    {
        public Task SendAsync(IdentityMessage message)
        {
            // Conecte el servicio SMS aquí para enviar un mensaje de texto.

            return configSendGridasync(message);
        }
        private async Task configSendGridasync(IdentityMessage message)
        {
            try
            {
                var client = new SendGridClient("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");
                var from = new EmailAddress("nonreply@gmail.com", "GreenHouse Monitor");
                var subject = message.Subject;
                var to = new EmailAddress(message.Destination);
                
                var htmlContent = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <style>
                        body {{
                            font-family: Arial, sans-serif;
                            line-height: 1.6;
                            color: #333;
                            max-width: 600px;
                            margin: 0 auto;
                            padding: 20px;
                        }}
                        .container {{
                            background-color: #f9f9f9;
                            border-radius: 8px;
                            padding: 20px;
                            margin: 20px 0;
                        }}
                        .header {{
                            background-color: #4CAF50;
                            color: white;
                            padding: 15px;
                            text-align: center;
                            border-radius: 8px 8px 0 0;
                        }}
                        .content {{
                            padding: 20px;
                            background-color: white;
                            border-radius: 0 0 8px 8px;
                            text-align: center;
                        }}
                        .button {{
                            display: inline-block;
                            padding: 12px 24px;
                            background-color: #4CAF50;
                            color: white;
                            text-decoration: none;
                            border-radius: 4px;
                            margin: 20px 0;
                            font-weight: bold;
                            border: none;
                            cursor: pointer;
                        }}
                        .button:hover {{
                            background-color: #45a049;
                        }}
                        .footer {{
                            text-align: center;
                            font-size: 12px;
                            color: #666;
                            margin-top: 20px;
                        }}
                    </style>
                </head>
                <body>
                    <div class='container'>
                        <div class='header'>
                            <h2>GreenHouse Monitor</h2>
                        </div>
                        <div class='content'>
                            <p>Por favor, confirma tu cuenta haciendo clic en el siguiente botón:</p>
                            <a href='{message.Body}' class='button'>Confirmar Cuenta</a>
                            <p style='margin-top: 20px;'>Si no has solicitado esta confirmación, puedes ignorar este correo.</p>
                        </div>
                    </div>
                    <div class='footer'>
                        <p>Este es un correo automático, por favor no responda a este mensaje.</p>
                    </div>
                </body>
                </html>";

                var msg = MailHelper.CreateSingleEmail(from, to, subject, $"Por favor, confirma tu cuenta visitando este enlace: {message.Body}", htmlContent);
                var response = await client.SendEmailAsync(msg);
            }
            catch (Exception ex) {
                var msgerror = ex.Message;
            }
        }

        
    }

    public class SmsService : IIdentityMessageService
    {
        public Task SendAsync(IdentityMessage message)
        {

            // Conecte el servicio de correo electrónico aquí para enviar un correo electrónico.
            return Task.FromResult(0);
        }
    }

    // Configure el administrador de usuarios de aplicación que se usa en esta aplicación. UserManager se define en ASP.NET Identity y se usa en la aplicación.
    public class ApplicationUserManager : UserManager<ApplicationUser>
    {
        public ApplicationUserManager(IUserStore<ApplicationUser> store)
            : base(store)
        {
        }

        public static ApplicationUserManager Create(IdentityFactoryOptions<ApplicationUserManager> options, IOwinContext context) 
        {
            var manager = new ApplicationUserManager(new UserStore<ApplicationUser>(context.Get<ApplicationDbContext>()));
            // Configure la lógica de validación de nombres de usuario
            manager.UserValidator = new UserValidator<ApplicationUser>(manager)
            {
                AllowOnlyAlphanumericUserNames = false,
                RequireUniqueEmail = true
            };

            // Configure la lógica de validación de contraseñas
            manager.PasswordValidator = new PasswordValidator
            {
                RequiredLength = 6,
                RequireNonLetterOrDigit = true,
                RequireDigit = true,
                RequireLowercase = true,
                RequireUppercase = true,
            };

            // Configurar valores predeterminados para bloqueo de usuario
            manager.UserLockoutEnabledByDefault = true;
            manager.DefaultAccountLockoutTimeSpan = TimeSpan.FromMinutes(5);
            manager.MaxFailedAccessAttemptsBeforeLockout = 5;

            // Registre los proveedores de autenticación de dos factores. Esta aplicación usa el teléfono y el correo electrónico para recibir un código de verificación del usuario
            // Puede escribir su propio proveedor y conectarlo aquí.
            manager.RegisterTwoFactorProvider("Código telefónico", new PhoneNumberTokenProvider<ApplicationUser>
            {
                MessageFormat = "Su código de seguridad es {0}"
            });
            manager.RegisterTwoFactorProvider("Código de correo electrónico", new EmailTokenProvider<ApplicationUser>
            {
                Subject = "Código de seguridad",
                BodyFormat = "Su código de seguridad es {0}"
            });
            manager.EmailService = new EmailService();
            manager.SmsService = new SmsService();
            var dataProtectionProvider = options.DataProtectionProvider;
            if (dataProtectionProvider != null)
            {
                manager.UserTokenProvider = 
                    new DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider.Create("ASP.NET Identity"));
            }
            return manager;
        }
    }

    // Configure el administrador de inicios de sesión que se usa en esta aplicación.
    public class ApplicationSignInManager : SignInManager<ApplicationUser, string>
    {
        public ApplicationSignInManager(ApplicationUserManager userManager, IAuthenticationManager authenticationManager)
            : base(userManager, authenticationManager)
        {
        }

        public override Task<ClaimsIdentity> CreateUserIdentityAsync(ApplicationUser user)
        {
            return user.GenerateUserIdentityAsync((ApplicationUserManager)UserManager);
        }

        public static ApplicationSignInManager Create(IdentityFactoryOptions<ApplicationSignInManager> options, IOwinContext context)
        {
            return new ApplicationSignInManager(context.GetUserManager<ApplicationUserManager>(), context.Authentication);
        }
    }
}
