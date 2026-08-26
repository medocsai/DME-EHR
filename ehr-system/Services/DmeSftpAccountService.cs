using EHR.Helpers;

namespace EHR.Services;

/// <summary>
/// The clearinghouse SFTP credentials: the only code that writes them and the
/// only code that will ever read them back.
///
/// WHY THIS EXISTS
/// A credential is not ordinary CRUD. Three rules have to hold everywhere and
/// they are the kind that get forgotten one call site at a time:
///
///   1. the username and the password are ciphertext at rest, both of them,
///      because a username is half a credential;
///   2. neither value ever reaches a screen, a JSON response or a log line;
///   3. a saved account starts in TEST mode, and going live is a deliberate act.
///
/// Putting them in one class means a future 837 sender inherits all three
/// instead of reimplementing two of them.
///
/// WHY THE ACCOUNT IS TENANT SCOPED AND NOT LOCATION SCOPED
/// Office Ally issues one SFTP account per billing entity. In this product a
/// billing entity is a tenant: no DME table carries a LocationId, so there is no
/// second entity for a credential to belong to. The account points at the
/// supplier profile, so if a supplier ever runs two separately accredited
/// DMEPOS locations, that becomes a pointer column rather than a rewrite. The
/// reasoning is in the migration header.
///
/// WHAT IT DELIBERATELY DOES NOT DO
/// There is no "test connection" and no decrypt path, because nothing transmits
/// yet. A decrypt method with no caller is an unguarded way to read a password
/// that exists only to look complete. It arrives with the 837 sender, which is
/// the thing that needs it.
///
/// WHO CALLS IT
/// DmeController, from the Settings screen. Admin roles only.
/// </summary>
public interface IDmeSftpAccountService
{
    /// <summary>
    /// Every account for the caller's tenant, WITHOUT the credentials. Reads
    /// vDmeSftpAccounts, which does not expose the two encrypted columns at all,
    /// so a screen cannot leak them by selecting too much.
    /// </summary>
    List<Dictionary<string, object?>> List();

    /// <summary>
    /// Create an account, or update one.
    ///
    /// On update a blank password means "leave the stored one alone". The
    /// alternative, re-typing the password to change the port, is how people end
    /// up writing it on a sticky note.
    /// </summary>
    Task<SftpAccountResult> SaveAsync(SftpAccountInput input);

    /// <summary>
    /// Take an account in or out of service. Never a delete: the row is the
    /// record of what past claims were submitted under.
    /// </summary>
    Task<SftpAccountResult> SetActiveAsync(int sftpAccountId, bool isActive);
}

/// <summary>
/// One account as the settings form supplies it. Password is nullable because
/// blank means "unchanged" on an update.
/// </summary>
public sealed record SftpAccountInput(
    int SftpAccountId,
    string Label,
    string Host,
    int Port,
    string? Username,
    string? Password,
    bool IsTestMode,
    bool IsActive);

/// <summary>Outcome of a save. Carries no credential material.</summary>
public sealed record SftpAccountResult(bool Ok, string? Error, int SftpAccountId = 0);

/// <inheritdoc cref="IDmeSftpAccountService"/>
public sealed class DmeSftpAccountService : IDmeSftpAccountService
{
    private readonly IDmeDb _db;
    private readonly IDmeAudit _audit;
    private readonly EncryptionHelper _crypto;

    public DmeSftpAccountService(IDmeDb db, IDmeAudit audit, EncryptionHelper crypto)
    {
        _db = db;
        _audit = audit;
        _crypto = crypto;
    }

    public List<Dictionary<string, object?>> List()
        => _db.Query("SELECT * FROM dbo.vDmeSftpAccounts ORDER BY IsActive DESC, Label");

    public async Task<SftpAccountResult> SaveAsync(SftpAccountInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Label))
            return new SftpAccountResult(false, "Give the account a name so you can tell it apart from another one.");
        if (string.IsNullOrWhiteSpace(input.Host))
            return new SftpAccountResult(false, "The SFTP host is required, for example ftp10.officeally.com.");
        if (input.Port is < 1 or > 65535)
            return new SftpAccountResult(false, "The port must be between 1 and 65535. Office Ally issues 22.");

        var existing = input.SftpAccountId > 0
            ? _db.QueryOne("SELECT SftpAccountId, Label, Host, Port, IsTestMode, IsActive FROM dbo.DmeSftpAccounts WHERE SftpAccountId=@id",
                new { id = input.SftpAccountId })
            : null;

        if (input.SftpAccountId > 0 && existing == null)
            return new SftpAccountResult(false, "That account no longer exists.");

        // A new account must carry both halves. An update may omit either to
        // leave it as stored.
        if (existing == null &&
            (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password)))
            return new SftpAccountResult(false, "A new account needs both the username and the password.");

        if (existing == null)
        {
            var id = Convert.ToInt32(_db.Scalar(@"
                INSERT INTO dbo.DmeSftpAccounts
                    (TenantId,Label,Host,Port,Username,Password,IsTestMode,IsActive,CreatedAt)
                OUTPUT inserted.SftpAccountId
                VALUES (@TenantId,@label,@host,@port,@username,@password,@isTest,@isActive,SYSUTCDATETIME())",
                new
                {
                    label = input.Label.Trim(),
                    host = input.Host.Trim(),
                    port = input.Port,
                    username = _crypto.Encrypt(input.Username!.Trim()),
                    password = _crypto.Encrypt(input.Password!),
                    isTest = input.IsTestMode,
                    isActive = input.IsActive,
                }));

            // The audit records the non-secret shape of what was stored, and
            // that a credential was set. Recording the credential itself would
            // move the secret into a table with a six year retention.
            await _audit.RecordAsync("DME_SFTP_ACCOUNT_CREATED", "DmeSftpAccount", id,
                before: null,
                after: new { input.Label, input.Host, input.Port, input.IsTestMode, input.IsActive, CredentialsSet = true });

            return new SftpAccountResult(true, null, id);
        }

        // Only overwrite a credential column when a new value was actually
        // supplied. COALESCE would not do: the parameter is deliberately null
        // for "unchanged", and passing an empty string must not blank a stored
        // password either.
        var setUsername = !string.IsNullOrWhiteSpace(input.Username);
        var setPassword = !string.IsNullOrWhiteSpace(input.Password);

        _db.Execute($@"
            UPDATE dbo.DmeSftpAccounts
               SET Label=@label, Host=@host, Port=@port,
                   IsTestMode=@isTest, IsActive=@isActive,
                   {(setUsername ? "Username=@username," : "")}
                   {(setPassword ? "Password=@password," : "")}
                   UpdatedAt=SYSUTCDATETIME()
             WHERE SftpAccountId=@id",
            new Dictionary<string, object?>
            {
                ["id"] = input.SftpAccountId,
                ["label"] = input.Label.Trim(),
                ["host"] = input.Host.Trim(),
                ["port"] = input.Port,
                ["isTest"] = input.IsTestMode,
                ["isActive"] = input.IsActive,
                ["username"] = setUsername ? _crypto.Encrypt(input.Username!.Trim()) : null,
                ["password"] = setPassword ? _crypto.Encrypt(input.Password!) : null,
            });

        await _audit.RecordAsync("DME_SFTP_ACCOUNT_UPDATED", "DmeSftpAccount", input.SftpAccountId,
            before: new
            {
                Label = F.S(existing["Label"]),
                Host = F.S(existing["Host"]),
                Port = F.I(existing["Port"]),
                IsTestMode = F.B(existing["IsTestMode"]),
                IsActive = F.B(existing["IsActive"]),
            },
            after: new
            {
                input.Label, input.Host, input.Port, input.IsTestMode, input.IsActive,
                UsernameChanged = setUsername, PasswordChanged = setPassword,
            });

        return new SftpAccountResult(true, null, input.SftpAccountId);
    }

    public async Task<SftpAccountResult> SetActiveAsync(int sftpAccountId, bool isActive)
    {
        var existing = _db.QueryOne(
            "SELECT Label, IsActive FROM dbo.DmeSftpAccounts WHERE SftpAccountId=@id", new { id = sftpAccountId });

        if (existing == null) return new SftpAccountResult(false, "That account no longer exists.");

        // Guarded on the current value so a repeated click does not write an
        // audit row describing a change that did not happen.
        var affected = _db.Execute(
            "UPDATE dbo.DmeSftpAccounts SET IsActive=@isActive, UpdatedAt=SYSUTCDATETIME() WHERE SftpAccountId=@id AND IsActive<>@isActive",
            new { id = sftpAccountId, isActive });

        if (affected == 0)
            return new SftpAccountResult(false, isActive ? "That account is already in service." : "That account is already out of service.");

        await _audit.RecordAsync(isActive ? "DME_SFTP_ACCOUNT_ACTIVATED" : "DME_SFTP_ACCOUNT_DEACTIVATED",
            "DmeSftpAccount", sftpAccountId,
            before: new { IsActive = F.B(existing["IsActive"]) },
            after: new { IsActive = isActive, Label = F.S(existing["Label"]) });

        return new SftpAccountResult(true, null, sftpAccountId);
    }
}
