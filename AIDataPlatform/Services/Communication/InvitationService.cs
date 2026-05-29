using AIDataPlatform.Data;
using AIDataPlatform.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static AIDataPlatform.Models.DataModel;

namespace AIDataPlatform.Services.Communication
{
    public class InvitationService(UserManager<ApplicationUser> userManager, EmailSenderService emailSenderService, ApplicationDbContext dbContext)
    {
        private readonly UserManager<ApplicationUser> userManager = userManager;
        private readonly EmailSenderService emailSenderService = emailSenderService;
        private readonly ApplicationDbContext dbContext = dbContext;

        public async Task<DataModel.Invitation> GetInvitationByTokenAsync(string token)
        {
            return await dbContext.Invitations.FirstOrDefaultAsync(i => i.Token == token && !i.IsAccepted && i.Expiration > DateTime.UtcNow);
        }

        public async Task InviteUserAsync(string email, string tenantId)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser { UserName = email, Email = email, TenantId = tenantId };
                await userManager.CreateAsync(user);
            }

            var token = await userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "Invitation");
            var encodedToken = Uri.EscapeDataString(token); // URL-encode the token

            var invitation = new DataModel.Invitation
            {
                Email = email,
                TenantId = tenantId,
                Token = token,
                Expiration = DateTime.UtcNow.AddDays(7),
                IsAccepted = false
            };

            dbContext.Invitations.Add(invitation);
            await dbContext.SaveChangesAsync();

            var invitationLink = $"/accept-invitation/{user.Id}/{encodedToken}";
            await emailSenderService.SendInvitationEmailAsync(email, invitationLink);
        }

        public async Task<bool> AcceptInvitationAsync(string userId, string token)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return false;
            }

            var invitation = await dbContext.Invitations.FirstOrDefaultAsync(i => i.Token == token && i.Email == user.Email && !i.IsAccepted && i.Expiration > DateTime.UtcNow);
            if (invitation == null)
            {
                return false;
            }

            var result = await userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, "Invitation", token);
            if (result)
            {
                user.EmailConfirmed = true;
                await userManager.UpdateAsync(user);

                invitation.IsAccepted = true;
                await dbContext.SaveChangesAsync();
            }

            return result;
        }

        public async Task<bool> SetUserPasswordAsync(string userId, string token, string password)
        {
            var user = await userManager.FindByIdAsync(userId);

            if (user == null)
            {
                return false;
            }

            var invitation = await dbContext.Invitations.FirstOrDefaultAsync(i => i.Token == token && i.Email == user.Email && !i.IsAccepted && i.Expiration > DateTime.UtcNow);
            if (invitation == null)
            {
                return false;
            }

            var result = await userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, "Invitation", token);
            if (result)
            {
                var passwordResult = await userManager.AddPasswordAsync(user, password);
                if (passwordResult.Succeeded)
                {
                    user.EmailConfirmed = true;

                    // Ensure TenantId is not null
                    if (string.IsNullOrEmpty(user.TenantId))
                    {
                        user.TenantId = invitation.TenantId;
                    }

                    await userManager.UpdateAsync(user);

                    invitation.IsAccepted = true;
                    await dbContext.SaveChangesAsync();
                    return true;
                }
            }

            return false;
        }
    }
}