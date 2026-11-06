using MoneySpend.Models;

namespace MoneySpend.Services;

public interface IAccountLinkService
{
    /// <summary>
    /// The account for this account number. Order: already linked -> an account you made yourself that
    /// obviously matches -> a NEW account, created automatically.
    /// </summary>
    Task<int?> ResolveAsync(string? hint, string? bankName);

    /// <summary>Names of accounts created since the last call (to tell the user).</summary>
    List<string> TakeNewlyCreated();
}

public class AccountLinkService : IAccountLinkService
{
    private readonly IGenericRepository<AccountLink> _links;
    private readonly IAccountService _accounts;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<string> _created = new();

    public AccountLinkService(IGenericRepository<AccountLink> links, IAccountService accounts)
    {
        _links = links;
        _accounts = accounts;
    }

    public async Task<int?> ResolveAsync(string? hint, string? bankName)
    {
        hint = hint?.Trim();
        if (string.IsNullOrEmpty(hint)) return null;

        if (hint.Equals(AccountNaming.GiftHint, StringComparison.OrdinalIgnoreCase))
            hint = AccountNaming.GiftHint;

        await _lock.WaitAsync();
        try
        {
            var accounts = await _accounts.GetAllAsync();   // active, not deleted
            var h = hint;

            // 1. already linked (and that account still exists)
            var link = (await _links.FindAsync(l => l.Hint == h)).FirstOrDefault();

            if (link is not null && accounts.FirstOrDefault(a => a.Id == link.AccountId) is { } linked)
            {
                // we only knew the digits before ("Bank ••5264"); now the bank is known -> use its name
                var better = string.IsNullOrWhiteSpace(bankName) || !bankName.Contains("bank", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : AccountNaming.NameFor(hint, AccountNaming.Tidy(bankName));

                if (better is not null && linked.Name == AccountNaming.NameFor(hint, null))
                {
                    linked.Name = better;
                    await _accounts.UpdateAsync(linked);
                }

                return link.AccountId;
            }

            // 2. an account you created yourself that clearly is this one
            var linkedToOthers = (await _links.FindAsync(l => l.Hint != h))
                .Select(l => l.AccountId)
                .ToHashSet();

            var accountId = AccountNaming.FindExisting(
                hint, bankName,
                accounts.Select(a => new AccountInfo(a.Id, a.Name, a.Type)).ToList(),
                linkedToOthers);

            // 3. nothing fits: create it
            if (accountId is null)
            {
                var account = new Account
                {
                    Name = AccountNaming.NameFor(hint, bankName),
                    Type = AccountNaming.TypeFor(hint),
                    OpeningBalance = 0,
                    CurrentBalance = 0,
                    IsActive = true
                };

                await _accounts.CreateAsync(account);

                // make sure we hold the saved row (with its Id)
                if (account.Id == 0)
                {
                    account = (await _accounts.GetAllAsync())
                        .OrderByDescending(a => a.Id)
                        .FirstOrDefault(a => a.Name == account.Name)
                        ?? throw new InvalidOperationException("Could not create the account.");
                }

                accountId = account.Id;
                _created.Add(account.Name);
            }

            if (link is null)
                await _links.AddAsync(new AccountLink { Hint = hint, AccountId = accountId.Value });
            else
            {
                // the linked account was deleted: point the number at the new one
                link.AccountId = accountId.Value;
                await _links.UpdateAsync(link);
            }

            return accountId;
        }
        finally
        {
            _lock.Release();
        }
    }

    public List<string> TakeNewlyCreated()
    {
        lock (_created)
        {
            var copy = _created.ToList();
            _created.Clear();
            return copy;
        }
    }
}
