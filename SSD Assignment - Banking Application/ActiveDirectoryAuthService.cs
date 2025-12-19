using System;
using System.DirectoryServices.AccountManagement;

namespace Banking_Application.Services
{
    
    public sealed class ActiveDirectoryAuthService
    {
        private readonly string domainName;

        public ActiveDirectoryAuthService(string domainName)
        {
            this.domainName = domainName ?? throw new ArgumentNullException(nameof(domainName));
        }

       
        public bool ValidateCredentials(string username, string password)
        {
            using var context = new PrincipalContext(ContextType.Domain, domainName);
            return context.ValidateCredentials(username, password);
        }

        
        public bool IsUserInGroup(string username, string groupName)
        {
            using var context = new PrincipalContext(ContextType.Domain, domainName);
            using var user = UserPrincipal.FindByIdentity(context, username);

            if (user == null)
                return false;

            using var group = GroupPrincipal.FindByIdentity(context, groupName);

            if (group == null)
                return false;

            return user.IsMemberOf(group);
        }
    }
}
