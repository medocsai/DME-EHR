#!/usr/bin/env bash
# ============================================================================
# DME foundation verification.
#
# Proves, by execution rather than by reading code, that the security and data
# integrity foundation is actually in place:
#
#   1. anonymous callers cannot read or write DME data
#   2. login issues an HttpOnly, SameSite=Strict session cookie
#   3. server-rendered pages authenticate off that cookie alone
#   4. customer PHI is ciphertext at rest and readable in the app
#   5. search still works over encrypted names (blind index)
#   6. tenant isolation holds for reads AND writes
#   7. derived values are computed, never stored
#   8. the audit trail reconstructs a change, and does not invent one
#   9. payments post, denials are separated from write-offs, and a void takes
#      every derived number back with it
#  10. the supplier identity is stored rather than hardcoded, and a clearinghouse
#      credential never appears in the database, the page or the audit log
#  11. branches separate the working data and roll back up, and location is
#      still not a security boundary
#  12. branch grants restrict the users who should be restricted, and an empty
#      grant set sees nothing rather than everything
#
# Usage:
#   cd ehr-system && dotnet run --urls http://localhost:5077
#   bash scripts/verify-dme-foundation.sh
#
# Section 10 also covers the Super Admin half (storing a clearinghouse
# credential, the clinic switcher scoping the server-rendered screens). That
# needs a role 0 sign in, supplied by environment variable and never defaulted
# here, because a Super Admin password committed to this repository would be a
# worse hole than anything the script checks:
#
#   SUPERADMIN_EMAIL=you@example.com SUPERADMIN_PASSWORD=... \
#     bash scripts/verify-dme-foundation.sh
#
# With them unset that half reports SKIP rather than silently counting as
# passed. 92 checks with them, 77 without.
#
# Requires sqlcmd and curl. Safe to re-run: it changes one claim status, which
# it restores, and creates claims, payments and a credential tagged with a
# marker, which it deletes. Signing in costs two of the five auth requests a
# minute the rate limiter allows, and the script signs in twice, so a back to
# back run waits the window out and retries once.
# ============================================================================
BASE=http://localhost:5077
SQL='sqlcmd -S localhost\SQLEXPRESS01 -E -C -d DMEEHR -h -1 -W'
J=cookies.txt
pass=0; fail=0
ok()  { echo "  PASS  $1"; pass=$((pass+1)); }
no()  { echo "  FAIL  $1"; fail=$((fail+1)); }
chk() { if [ "$2" = "$3" ]; then ok "$1 ($2)"; else no "$1 (expected $3, got $2)"; fi; }

echo "=============================================================="
echo " 1. ANONYMOUS ACCESS IS REFUSED"
echo "=============================================================="
rm -f $J
for p in /Dme/Dashboard /Dme/Customers /Dme/Customer/1 /Dme/Orders /Dme/Billing /Hcpcs; do
  chk "anon GET $p redirects to login" "$(curl -s -o /dev/null -w '%{http_code}' -H 'Accept: text/html' $BASE$p)" "302"
done
chk "anon API-style GET is 401" "$(curl -s -o /dev/null -w '%{http_code}' -H 'Accept: application/json' $BASE/Dme/Customers)" "401"
chk "anon POST /Dme/Submit is 401" "$(curl -s -o /dev/null -w '%{http_code}' -X POST $BASE/Dme/Submit -d 'id=3')" "401"
chk "anon page leaks no PHI" "$(curl -s -H 'Accept: text/html' $BASE/Dme/Customers | grep -cE 'Margaret|LMS-1001')" "0"

echo
echo "=============================================================="
echo " 2. LOGIN ISSUES AN HttpOnly SESSION COOKIE"
echo "=============================================================="
# Signing in costs two of the five requests a minute the auth rate limiter
# allows per address, and this script signs in twice. Running it back to back
# therefore trips the limiter, and a 429 here cascades into dozens of unrelated
# failures further down because nothing after this point has a session. Wait the
# window out and retry once rather than reporting a broken product.
ca_signin() {
  rm -f $J
  CA_LOGIN=$(curl -s -c $J -o /dev/null -w '%{http_code}' -X POST $BASE/api/auth/login \
       -H 'Content-Type: application/json' -d '{"Email":"admin@md.com","Password":"DemoPass@2026"}')
  # Headers AND body: the cookie proves section 2's point, and the bearer token
  # is kept for section 11, which calls the location switch API. Signing in
  # again there would be a third sign in and would trip the limiter this whole
  # helper exists to survive.
  HDRS=$(curl -s -c $J -b $J -D - -o /tmp/dme-verify.json -X POST $BASE/api/auth/verify-otp \
       -H 'Content-Type: application/json' -d '{"Email":"admin@md.com","OtpCode":"123456"}')
  CA_TOKEN=$(grep -oE '"Token":"[^"]+' /tmp/dme-verify.json | sed 's/"Token":"//')
  echo "$HDRS" | grep -qi 'set-cookie:.*__medocs_sess'
}

if ! ca_signin; then
  echo "  INFO  auth rate limit or transient failure, waiting out the window and retrying once"
  sleep 61
  ca_signin
fi
echo "$HDRS" | grep -qi 'set-cookie:.*__medocs_sess'      && ok "session cookie issued"      || no "session cookie issued"
echo "$HDRS" | grep -i  'set-cookie:.*__medocs_sess' | grep -qi 'httponly' && ok "cookie is HttpOnly" || no "cookie is HttpOnly"
echo "$HDRS" | grep -i  'set-cookie:.*__medocs_sess' | grep -qi 'samesite=strict' && ok "cookie is SameSite=Strict" || no "cookie is SameSite=Strict"

echo
echo "=============================================================="
echo " 3. AUTHENTICATED PAGES WORK (cookie only, no Authorization header)"
echo "=============================================================="
for p in /Dme/Dashboard /Dme/Customers /Dme/Customer/1 /Dme/Orders /Dme/Order/1 /Dme/Rentals \
         /Dme/Billing /Dme/Cms/1 /Dme/Inventory /Dme/Schedule /Dme/NewCustomer /Dme/NewOrder /Hcpcs; do
  chk "GET $p" "$(curl -s -b $J -o /dev/null -w '%{http_code}' -H 'Accept: text/html' $BASE$p)" "200"
done

echo
echo "=============================================================="
echo " 4. PHI IS ENCRYPTED AT REST, READABLE IN THE APP"
echo "=============================================================="
CIPHER=$($SQL -Q "SET NOCOUNT ON; SELECT LEFT(FirstName,12) FROM dbo.DmeCustomers WHERE CustomerId=2" | tr -d ' \r')
if [ "$CIPHER" = "Margaret" ]; then no "DmeCustomers.FirstName is ciphertext (still plaintext: run BackfillPhi)"; else ok "DmeCustomers.FirstName is ciphertext ($CIPHER...)"; fi
if [ "$(curl -s -b $J $BASE/Dme/Customer/2 | grep -c 'Margaret')" -ge 1 ]; then ok "app renders the decrypted name"; else no "app renders the decrypted name"; fi

echo
echo "=============================================================="
echo " 5. SEARCH STILL WORKS OVER ENCRYPTED NAMES (blind index)"
echo "=============================================================="
if [ "$(curl -s -b $J "$BASE/Dme/Customers?q=mar" | grep -c 'LMS-1002')" -ge 1 ]; then ok "prefix mar finds Margaret Ellis"; else no "prefix mar finds Margaret Ellis"; fi
if [ "$(curl -s -b $J "$BASE/Dme/Customers?q=ellis" | grep -c 'LMS-1002')" -ge 1 ]; then ok "surname ellis finds Margaret Ellis"; else no "surname ellis finds Margaret Ellis"; fi
if [ "$(curl -s -b $J "$BASE/Dme/Customers?q=2145550144" | grep -c 'LMS-1002')" -ge 1 ]; then ok "phone digits find the customer"; else no "phone digits find the customer"; fi
if [ "$(curl -s -b $J "$BASE/Dme/Customers?q=LMS-1003" | grep -c 'LMS-1003')" -ge 1 ]; then ok "account number still searchable"; else no "account number still searchable"; fi
chk "nonsense term finds nobody"           "$(curl -s -b $J "$BASE/Dme/Customers?q=zzzznope"   | grep -cE 'LMS-100[0-9]')" "0"

echo
echo "=============================================================="
echo " 6. TENANT ISOLATION (database level)"
echo "=============================================================="
V1=$($SQL -Q "SET NOCOUNT ON; EXEC sp_set_session_context N'CurrentTenantId', 1; SELECT COUNT(*) FROM dbo.DmeCustomers" | tr -d ' \r')
V2=$($SQL -Q "SET NOCOUNT ON; EXEC sp_set_session_context N'CurrentTenantId', 2; SELECT COUNT(*) FROM dbo.DmeCustomers" | tr -d ' \r')
chk "tenant 1 sees its customers" "$V1" "5"
chk "tenant 2 sees none of them"  "$V2" "0"
BLOCKED=$($SQL -Q "SET NOCOUNT ON; EXEC sp_set_session_context N'CurrentTenantId', 1;
BEGIN TRY INSERT INTO dbo.DmeCustomers (AccountNo,FirstName,LastName,Status,TenantId) VALUES ('X','a','b','active',2); SELECT 'NOT-BLOCKED'; END TRY
BEGIN CATCH SELECT 'BLOCKED'; END CATCH" | tr -d ' \r')
chk "cross-tenant write is blocked" "$BLOCKED" "BLOCKED"
RLS=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(DISTINCT sp.target_object_id) FROM sys.security_predicates sp JOIN sys.security_policies p ON p.object_id=sp.object_id WHERE p.name='TenantIsolationPolicy'" | tr -d ' \r')
echo "  INFO  tables protected by row level security: $RLS"

echo
echo "=============================================================="
echo " 7. DERIVED VALUES ARE COMPUTED, NOT STORED"
echo "=============================================================="
STORED=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id
WHERE (t.name='DmeRentals' AND c.name IN ('MonthsBilled','CustomerName'))
   OR (t.name='DmeOrders'  AND c.name IN ('CustomerName','DoctorName'))
   OR (t.name='DmeClaims'  AND c.name='Total')
   OR (t.name='HcpcsCodes' AND c.name='OnHand')" | tr -d ' \r')
chk "no stored derived columns remain" "$STORED" "0"
MISMATCH=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.vDmeClaims v WHERE v.Total <> (SELECT ISNULL(SUM(Charge),0) FROM dbo.DmeClaimLines l WHERE l.ClaimId=v.ClaimId)" | tr -d ' \r')
chk "claim totals equal the sum of their lines" "$MISMATCH" "0"
echo "  INFO  rentals (months billed is now a count of linked claim lines):"
$SQL -Q "SET NOCOUNT ON; SELECT CONCAT('        rental ', RentalId, ' ', Hcpcs, ' -> ', MonthsBilled, '/', CapMonths) FROM dbo.vDmeRentals ORDER BY RentalId"

echo
echo "=============================================================="
echo " 8. AUDIT TRAIL RECONSTRUCTS THE CHANGE"
echo "=============================================================="
BEFORE=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.AuditLogs WHERE Action='DME_CLAIM_SUBMITTED'" | tr -d ' \r')
TOK=$(curl -s -b $J -c $J $BASE/Dme/Billing | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+"' | head -1 | sed 's/.*value="//;s/"//')
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/Submit -d "id=3&__RequestVerificationToken=$TOK"
AFTER=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.AuditLogs WHERE Action='DME_CLAIM_SUBMITTED'" | tr -d ' \r')
chk "submitting a claim writes an audit row" "$AFTER" "$((BEFORE+1))"
ROW=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 CONCAT(UserEmail,' | ',OldValues,' -> ',NewValues,' | ',IpAddress) FROM dbo.AuditLogs WHERE Action='DME_CLAIM_SUBMITTED' ORDER BY AuditId DESC")
echo "  INFO  $ROW"
BEFORE2=$AFTER
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/Submit -d "id=3&__RequestVerificationToken=$TOK"
AFTER2=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.AuditLogs WHERE Action='DME_CLAIM_SUBMITTED'" | tr -d ' \r')
chk "a no-op does NOT fabricate an audit row" "$AFTER2" "$BEFORE2"
$SQL -Q "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; UPDATE dbo.DmeClaims SET Status='ready' WHERE ClaimId=3" > /dev/null
echo "  INFO  claim 3 restored to 'ready'"

echo
echo "=============================================================="
echo " 9. PAYMENTS AND DENIALS"
echo "=============================================================="
# This section works against a claim it creates itself, tagged with a marker and
# deleted at the end. Asserting against the seeded demo claims looked simpler and
# was wrong: the numbers then depend on how much demo data happens to be in the
# database, so the script passed on a fresh copy and failed on a used one.
MARKER='verify-dme-foundation'
CUST=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 CustomerId FROM dbo.DmeCustomers ORDER BY CustomerId" | tr -d ' \r')
TODAY=$(date +%Y-%m-%d)

$SQL -Q "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON;
INSERT INTO dbo.DmeClaims (ClaimNumber,CustomerId,PayerName,Status,ServiceDate,TenantId)
VALUES ('CLM-VERIFY',$CUST,'$MARKER','ready','$TODAY',1);
DECLARE @c INT = SCOPE_IDENTITY();
INSERT INTO dbo.DmeClaimLines (ClaimId,Hcpcs,ItemName,Modifier,Units,Charge,TenantId) VALUES
 (@c,'E1390','Oxygen Concentrator','RR',1,178.00,1),
 (@c,'A4253','Test Strips','NU',1,80.00,1);" > /dev/null

CV=$($SQL -Q "SET NOCOUNT ON; SELECT ClaimId FROM dbo.DmeClaims WHERE ClaimNumber='CLM-VERIFY'" | tr -d ' \r')
CLA=$($SQL -Q "SET NOCOUNT ON; SELECT MIN(ClaimLineId) FROM dbo.DmeClaimLines WHERE ClaimId=$CV" | tr -d ' \r')
CLB=$($SQL -Q "SET NOCOUNT ON; SELECT MAX(ClaimLineId) FROM dbo.DmeClaimLines WHERE ClaimId=$CV" | tr -d ' \r')

for p in /Dme/Payments "/Dme/PostPayment/$CV"; do
  chk "GET $p" "$(curl -s -b $J -o /dev/null -w '%{http_code}' -H 'Accept: text/html' $BASE$p)" "200"
done

paytok()  { curl -s -b $J -c $J "$BASE/Dme/PostPayment/$1" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+"' | head -1 | sed 's/.*value="//;s/"//'; }
paycount(){ $SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmePayments" | tr -d ' \r'; }
claim()   { $SQL -Q "SET NOCOUNT ON; SELECT CAST($1 AS NVARCHAR(50)) FROM dbo.vDmeClaims WHERE ClaimNumber='CLM-VERIFY'" | tr -d ' \r'; }

# --- a normal Medicare remittance on line A: allowed 142.40, pays 80 percent,
#     the remaining 20 percent becomes the patient's coinsurance
TOK=$(paytok $CV)
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreatePayment \
  --data-urlencode "__RequestVerificationToken=$TOK" \
  -d "claimId=$CV" -d 'source=payer' --data-urlencode "payerName=$MARKER" \
  -d "postedDate=$TODAY" -d 'method=eft' -d 'referenceNumber=EFT-VERIFY' -d 'amount=113.92' \
  --data-urlencode "note=$MARKER" \
  -d "claimLineId=$CLA" -d 'allowed=142.40' -d 'paid=113.92' \
  -d "claimLineId=$CLB" -d 'allowed=0'      -d 'paid=0' \
  -d 'adjLine=0' -d 'adjGroup=CO' -d 'adjCode=45' -d 'adjAmount=35.60' \
  -d 'adjLine=0' -d 'adjGroup=PR' -d 'adjCode=2'  -d 'adjAmount=28.48'

chk "paid to date is the amount posted"         "$(claim PaidTotal)"        "113.92"
chk "the contractual discount is kept apart"    "$(claim ContractualTotal)" "35.60"
chk "the patient coinsurance is what is left"   "$(claim PatientBalance)"   "28.48"
# Checked against a SEEDED claim, not the scratch one: the scratch claim is
# created straight in SQL and therefore has no encrypted name to decrypt, so it
# could never catch the base64 rendering bug this guards against.
CSEED=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 ClaimId FROM dbo.DmeClaims WHERE CustomerName IS NOT NULL ORDER BY ClaimId" | tr -d ' \r')
if [ "$(curl -s -b $J "$BASE/Dme/PostPayment/$CSEED" | grep -cE 'Margaret|John|Robert|Dolores|Frank')" -ge 1 ]; then ok "the posting screen renders the decrypted customer name"; else no "the posting screen renders the decrypted customer name (base64?)"; fi
if [ "$(curl -s -b $J "$BASE/Dme/Payments" | grep -cE 'Margaret|John|Robert|Dolores|Frank|Medicare|Aetna')" -ge 1 ]; then ok "the payments list renders names, not ciphertext"; else no "the payments list renders names, not ciphertext"; fi

# --- the customer then pays their own share in cash, into the same model
TOK=$(paytok $CV)
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreatePayment \
  --data-urlencode "__RequestVerificationToken=$TOK" \
  -d "claimId=$CV" -d 'source=customer' \
  -d "postedDate=$TODAY" -d 'method=cash' -d 'amount=28.48' \
  --data-urlencode "note=$MARKER" \
  -d "claimLineId=$CLA" -d 'allowed=0' -d 'paid=28.48'

chk "customer money is tracked separately" "$(claim CustomerPaidTotal)" "28.48"
chk "the patient balance clears"           "$(claim PatientBalance)"    "0.00"

# --- line B is refused: a zero dollar remittance that explains itself
TOK=$(paytok $CV)
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreatePayment \
  --data-urlencode "__RequestVerificationToken=$TOK" \
  -d "claimId=$CV" -d 'source=payer' --data-urlencode "payerName=$MARKER" \
  -d "postedDate=$TODAY" -d 'method=eft' -d 'referenceNumber=EFT-DENY' -d 'amount=0' \
  --data-urlencode "note=$MARKER" \
  -d "claimLineId=$CLB" -d 'allowed=0' -d 'paid=0' \
  -d 'adjLine=0' -d 'adjGroup=CO' -d 'adjCode=197' -d 'adjAmount=80.00'

# The distinction the whole model exists for. Line A carried a CO-45 discount of
# 35.60 and was NOT denied; only line B was. A design that counted "billed minus
# paid" would report 115.60 here and the client would stop trusting the tile.
chk "only the refused line counts as denied" "$(claim DeniedCharge)"  "80.00"
# A part denied claim settles to a zero balance, so it must not read as paid or
# the refused line is never appealed and the appeal window quietly expires.
chk "a partly denied claim says so"          "$(claim PaymentStatus)" "part-denied"

# --- the screens agree with the database, to the cent
MONTH_START=$(date +%Y-%m-01)
MONTHSQL="FROM dbo.vDmePaymentLines WHERE IsVoided=0 AND PostedDate >= '$MONTH_START' AND PostedDate < DATEADD(month,1,'$MONTH_START')"
EXP_PAID=$($SQL -Q "SET NOCOUNT ON; SELECT FORMAT(ISNULL(SUM(PaidAmount),0),'N2') $MONTHSQL" | tr -d ' \r')
EXP_DENY=$($SQL -Q "SET NOCOUNT ON; SELECT FORMAT(ISNULL(SUM(CASE WHEN IsDenied=1 THEN Charge ELSE 0 END),0),'N2') $MONTHSQL" | tr -d ' \r')
EXP_CODE=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 DenialCode $MONTHSQL AND IsDenied=1 GROUP BY DenialCode ORDER BY COUNT(*) DESC, DenialCode" | tr -d ' \r')
DASH=$(curl -s -b $J "$BASE/Dme/Dashboard")
echo "$DASH" | grep -q "\$$EXP_PAID" && ok "amount paid tile matches the database ($EXP_PAID)"   || no "amount paid tile matches the database (expected $EXP_PAID)"
echo "$DASH" | grep -q "\$$EXP_DENY" && ok "amount denied tile matches the database ($EXP_DENY)" || no "amount denied tile matches the database (expected $EXP_DENY)"
echo "$DASH" | grep -q "$EXP_CODE"   && ok "most frequent denial code matches the database ($EXP_CODE)" || no "most frequent denial code matches the database (expected $EXP_CODE)"
echo "$DASH" | grep -q 'posted'      && ok "the screen states which date it counts by" || no "the screen states which date it counts by"

# --- the rules hold against a hand-rolled POST, not just against the form
COUNT_BEFORE=$(paycount)
TOK=$(paytok $CV)
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreatePayment \
  --data-urlencode "__RequestVerificationToken=$TOK" \
  -d "claimId=$CV" -d 'source=payer' -d "postedDate=$TODAY" -d 'method=check' -d 'amount=10' \
  -d "claimLineId=$CLA" -d 'allowed=0' -d 'paid=500'
chk "applying more than the receipt is refused server side" "$(paycount)" "$COUNT_BEFORE"

FUTURE=$(date -d '+3 days' +%Y-%m-%d 2>/dev/null || date -v+3d +%Y-%m-%d)
TOK=$(paytok $CV)
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreatePayment \
  --data-urlencode "__RequestVerificationToken=$TOK" \
  -d "claimId=$CV" -d 'source=payer' -d "postedDate=$FUTURE" -d 'method=check' -d 'amount=10' \
  -d "claimLineId=$CLA" -d 'allowed=10' -d 'paid=10'
chk "a future posting date is refused server side" "$(paycount)" "$COUNT_BEFORE"

# --- voiding must take every derived number back with it
DENY_ID=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 PaymentId FROM dbo.DmePayments WHERE ReferenceNumber='EFT-DENY' AND Note='$MARKER' ORDER BY PaymentId DESC" | tr -d ' \r')
TOK=$(curl -s -b $J -c $J "$BASE/Dme/Payments" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+"' | head -1 | sed 's/.*value="//;s/"//')
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/VoidPayment \
  --data-urlencode "__RequestVerificationToken=$TOK" -d "id=$DENY_ID" --data-urlencode 'reason=verification run'
chk "a voided denial stops counting"      "$(claim DeniedCharge)"  "0.00"
# Back to 'partial', not 'paid': line B was never adjudicated once its denial
# was voided, so the payer still owes its 80.00 and the claim is correctly still
# open. A void that left it reading 'paid' would hide an unbilled line forever.
chk "the claim goes back to still owed"   "$(claim PaymentStatus)"    "partial"
chk "the voided line is owed by the payer" "$(claim InsuranceBalance)" "80.00"
chk "the voided payment row still exists" "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmePayments WHERE PaymentId=$DENY_ID" | tr -d ' \r')" "1"

AUDITED=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.AuditLogs WHERE Action IN ('DME_PAYMENT_POSTED','DME_PAYMENT_VOIDED')" | tr -d ' \r')
if [ "$AUDITED" -ge 4 ]; then ok "posting and voiding are audited ($AUDITED rows)"; else no "posting and voiding are audited (got $AUDITED)"; fi

# --- clean up everything this section created
$SQL -Q "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON;
DELETE a FROM dbo.DmePaymentLineAdjustments a JOIN dbo.DmePaymentLines l ON l.PaymentLineId=a.PaymentLineId JOIN dbo.DmePayments p ON p.PaymentId=l.PaymentId WHERE p.Note='$MARKER';
DELETE l FROM dbo.DmePaymentLines l JOIN dbo.DmePayments p ON p.PaymentId=l.PaymentId WHERE p.Note='$MARKER';
DELETE FROM dbo.DmePayments WHERE Note='$MARKER';
DELETE FROM dbo.DmeClaimLines WHERE ClaimId=$CV;
DELETE FROM dbo.DmeClaims WHERE ClaimNumber='CLM-VERIFY';" > /dev/null
chk "verification payments cleaned up" "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmePayments WHERE Note='$MARKER'" | tr -d ' \r')" "0"
chk "verification claim cleaned up"    "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeClaims WHERE ClaimNumber='CLM-VERIFY'" | tr -d ' \r')" "0"


echo "=============================================================="
echo " 10. SUPPLIER IDENTITY AND CLEARINGHOUSE CREDENTIALS"
echo "=============================================================="
SMARK='verify-sftp'

chk "GET /Dme/Settings" "$(curl -s -b $J -o /dev/null -w '%{http_code}' -H 'Accept: text/html' $BASE/Dme/Settings)" "200"

# The CMS-1500 must bill as the supplier record, not as a string in the markup.
PROVNAME=$($SQL -Q "SET NOCOUNT ON; SELECT BillingName FROM dbo.vDmeBillingProvider WHERE TenantId=1" | tr -d '\r' | sed 's/ *$//')
PROVNPI=$($SQL -Q "SET NOCOUNT ON; SELECT Npi FROM dbo.vDmeBillingProvider WHERE TenantId=1" | tr -d ' \r')
CMS=$(curl -s -b $J "$BASE/Dme/Cms/1")
echo "$CMS" | grep -q "$PROVNAME" && ok "CMS-1500 box 33 shows the supplier record ($PROVNAME)" || no "CMS-1500 box 33 shows the supplier record (expected $PROVNAME)"
echo "$CMS" | grep -q "$PROVNPI"  && ok "CMS-1500 box 33a shows the stored NPI ($PROVNPI)"      || no "CMS-1500 box 33a shows the stored NPI (expected $PROVNPI)"
echo "$CMS" | grep -q '1980000000' && no "the old hardcoded NPI is gone" || ok "the old hardcoded NPI is gone"
echo "$CMS" | grep -q '17b NPI'   && ok "CMS-1500 renders box 17b, mandatory on DMEPOS"        || no "CMS-1500 renders box 17b, mandatory on DMEPOS"
echo "$CMS" | grep -q 'Dr\.'      && ok "CMS-1500 names the ordering physician"                || no "CMS-1500 names the ordering physician"

# --- the clearinghouse credential is Medocs work, not the customer's
# The session above is a CLINIC ADMIN. That is the important half of this
# section: the checks below prove the refusal, and they always run.
CA_SETTINGS=$(curl -s -b $J "$BASE/Dme/Settings")
echo "$CA_SETTINGS" | grep -q 'Add account' && no "a clinic admin is not shown the credential form" || ok "a clinic admin is not shown the credential form"
echo "$CA_SETTINGS" | grep -q 'set up and maintained by Medocs' && ok "a clinic admin is told who maintains it" || no "a clinic admin is told who maintains it"

CATOK=$(curl -s -b $J -c $J "$BASE/Dme/Settings" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+"' | head -1 | sed 's/.*value="//;s/"//')
chk "a clinic admin POSTing a credential is refused" \
  "$(curl -s -b $J -c $J -o /dev/null -w '%{http_code}' -X POST $BASE/Dme/SaveSftpAccount \
      --data-urlencode "__RequestVerificationToken=$CATOK" \
      -d 'sftpAccountId=0' --data-urlencode "label=$SMARK-clinicadmin" -d 'host=x' -d 'port=22' \
      -d 'username=u' -d 'password=p' -d 'isActive=true')" "403"
chk "and nothing was written" "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeSftpAccounts WHERE Label='$SMARK-clinicadmin'" | tr -d ' \r')" "0"

# --- the super admin half
#
# Credentials come from the environment and are NEVER defaulted here. A super
# admin password committed to the repository would be a worse hole than
# anything this script checks. With nothing set the section reports SKIP, which
# is honest: it does not quietly count as passed.
if [ -z "$SUPERADMIN_EMAIL" ] || [ -z "$SUPERADMIN_PASSWORD" ]; then
  echo "  SKIP  credential storage as super admin"
  echo "        set SUPERADMIN_EMAIL and SUPERADMIN_PASSWORD to run it."
  echo "        The encryption itself is covered by DmeClearinghouseCredentialTests."
else
  # Sign in, retrying once through the auth rate limiter.
  #
  # The limiter allows 5 requests a minute per address, and a clean run of this
  # script spends 4 of them (a sign in each for the clinic admin and the super
  # admin, two calls apiece). Running the script twice inside a minute therefore
  # trips it, and a 429 here used to surface as five unrelated checks reporting
  # 302, which reads like a broken feature rather than a throttled login.
  S=sa-cookies.txt
  sa_signin() {
    rm -f $S
    SA_LOGIN=$(curl -s -c $S -o /dev/null -w '%{http_code}' -X POST $BASE/api/auth/login -H 'Content-Type: application/json' \
         -d "{\"Email\":\"$SUPERADMIN_EMAIL\",\"Password\":\"$SUPERADMIN_PASSWORD\"}")
    SA_VERIFY=$(curl -s -c $S -b $S -o /dev/null -w '%{http_code}' -X POST $BASE/api/auth/verify-otp -H 'Content-Type: application/json' \
         -d "{\"Email\":\"$SUPERADMIN_EMAIL\",\"OtpCode\":\"123456\"}")
    grep -q '__medocs_sess' $S 2>/dev/null
  }

  if ! sa_signin; then
    if [ "$SA_LOGIN" = "429" ] || [ "$SA_VERIFY" = "429" ]; then
      echo "  INFO  auth rate limit hit, waiting out the window and retrying once"
      sleep 61
      sa_signin
    fi
  fi

  if grep -q '__medocs_sess' $S 2>/dev/null; then
    ok "super admin signed in"
  else
    no "super admin sign in (login HTTP $SA_LOGIN, verify HTTP $SA_VERIFY, no session cookie)"
  fi

  chk "super admin reaches a chosen clinic" \
    "$(curl -s -b $S -o /dev/null -w '%{http_code}' -H 'Accept: text/html' "$BASE/Dme/Settings?tenantId=1")" "200"
  chk "super admin with no clinic chosen is redirected, not 500" \
    "$(curl -s -b $S -o /dev/null -w '%{http_code}' -H 'Accept: text/html' "$BASE/Dme/Settings")" "302"
  chk "the clinic switcher cookie scopes the DME screens" \
    "$(curl -s -b $S -b 'medocs_clinic=1' -o /dev/null -w '%{http_code}' -H 'Accept: text/html' "$BASE/Dme/Settings")" "200"

  # THE security property: a clinic admin's token pins them to their own tenant,
  # so forging the switcher cookie must move nothing.
  OWN=$(curl -s -b $J -b 'medocs_clinic=3' "$BASE/Dme/Settings" | grep -oE 'name="billingName"[^>]*value="[^"]*"' | head -1 | sed 's/.*value="//;s/"//')
  chk "a forged clinic cookie cannot move a clinic admin" "$OWN" "Lakeview Medical Supply"

  SATOK=$(curl -s -b $S -c $S "$BASE/Dme/Settings?tenantId=1" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+"' | head -1 | sed 's/.*value="//;s/"//')
  curl -s -b $S -c $S -o /dev/null -X POST "$BASE/Dme/SaveSftpAccount?tenantId=1" \
    --data-urlencode "__RequestVerificationToken=$SATOK" \
    -d 'sftpAccountId=0' --data-urlencode "label=$SMARK" -d 'host=ftp10.officeally.com' -d 'port=22' \
    -d 'username=VERIFYUSER1' -d 'password=verify-secret-9x' -d 'isTestMode=true' -d 'isActive=true'

  SID=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 SftpAccountId FROM dbo.DmeSftpAccounts WHERE Label='$SMARK' ORDER BY SftpAccountId DESC" | tr -d ' \r')
  chk "super admin can store a credential" "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeSftpAccounts WHERE Label='$SMARK'" | tr -d ' \r')" "1"

  PLAIN=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeSftpAccounts WHERE Label='$SMARK' AND (Username='VERIFYUSER1' OR Password='verify-secret-9x')" | tr -d ' \r')
  chk "the credential is ciphertext at rest" "$PLAIN" "0"
  echo "  INFO  stored username begins: $($SQL -Q "SET NOCOUNT ON; SELECT LEFT(Username,10) FROM dbo.DmeSftpAccounts WHERE Label='$SMARK'" | tr -d ' \r')..."

  SASETTINGS=$(curl -s -b $S "$BASE/Dme/Settings?tenantId=1")
  echo "$SASETTINGS" | grep -q 'VERIFYUSER1'      && no "the username never reaches the page" || ok "the username never reaches the page"
  echo "$SASETTINGS" | grep -q 'verify-secret-9x' && no "the password never reaches the page" || ok "the password never reaches the page"
  echo "$SASETTINGS" | grep -q "$SMARK"           && ok "the account is listed by its label"  || no "the account is listed by its label"
  echo "$SASETTINGS" | grep -q 'Stored'           && ok "the page reports that a credential exists" || no "the page reports that a credential exists"

  AUDLEAK=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.AuditLogs WHERE (NewValues LIKE '%VERIFYUSER1%' OR NewValues LIKE '%verify-secret-9x%')" | tr -d ' \r')
  chk "the credential is not copied into the audit log" "$AUDLEAK" "0"
  AUDROW=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.AuditLogs WHERE Action='DME_SFTP_ACCOUNT_CREATED'" | tr -d ' \r')
  if [ "$AUDROW" -ge 1 ]; then ok "storing a credential is audited"; else no "storing a credential is audited"; fi

  chk "a new account starts in test mode" "$($SQL -Q "SET NOCOUNT ON; SELECT CAST(IsTestMode AS INT) FROM dbo.DmeSftpAccounts WHERE Label='$SMARK'" | tr -d ' \r')" "1"

  SATOK=$(curl -s -b $S -c $S "$BASE/Dme/Settings?tenantId=1" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+"' | head -1 | sed 's/.*value="//;s/"//')
  curl -s -b $S -c $S -o /dev/null -X POST "$BASE/Dme/SetSftpActive?tenantId=1" \
    --data-urlencode "__RequestVerificationToken=$SATOK" -d "id=$SID" -d 'isActive=false'
  chk "taking an account out of service keeps the row" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT CONCAT(COUNT(*),'/',SUM(CAST(IsActive AS INT))) FROM dbo.DmeSftpAccounts WHERE SftpAccountId=$SID" | tr -d ' \r')" "1/0"

  # A clinic admin cannot create the WEAKEST kind of admin either: the clinic
  # creation route was the fifth place a staff password is set and the only one
  # that checked nothing.
  rm -f $S
fi
# --- a claim cannot be filed under a supplier that does not identify anybody
$SQL -Q "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; UPDATE dbo.Tenants SET NPI='' WHERE TenantId=1;" > /dev/null
READY=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 ClaimId FROM dbo.DmeClaims WHERE Status='ready' ORDER BY ClaimId" | tr -d ' \r')
BTOK=$(curl -s -b $J -c $J "$BASE/Dme/Billing" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+"' | head -1 | sed 's/.*value="//;s/"//')
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/Submit --data-urlencode "__RequestVerificationToken=$BTOK" -d "id=$READY"
chk "a claim cannot be submitted with no NPI on file" "$($SQL -Q "SET NOCOUNT ON; SELECT Status FROM dbo.DmeClaims WHERE ClaimId=$READY" | tr -d ' \r')" "ready"
$SQL -Q "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; UPDATE dbo.Tenants SET NPI='$PROVNPI' WHERE TenantId=1;" > /dev/null
chk "the supplier NPI is restored" "$($SQL -Q "SET NOCOUNT ON; SELECT Npi FROM dbo.vDmeBillingProvider WHERE TenantId=1" | tr -d ' \r')" "$PROVNPI"

# --- the settings screen is not for everyone
chk "settings refuses an anonymous caller" "$(curl -s -o /dev/null -w '%{http_code}' -H 'Accept: text/html' $BASE/Dme/Settings)" "302"

# --- clean up
$SQL -Q "SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON; DELETE FROM dbo.DmeSftpAccounts WHERE Label LIKE '$SMARK%';" > /dev/null
chk "verification credential cleaned up" "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeSftpAccounts WHERE Label LIKE '$SMARK%'" | tr -d ' \r')" "0"

echo "=============================================================="
echo " 11. LOCATIONS: SEPARATE THE BRANCHES, ROLL THEM UP AGAIN"
echo "=============================================================="
# A DME supplier with several branches is one business. Location separates the
# working data; the TENANT is what keeps suppliers apart. These checks prove
# both halves, and that the second one has not quietly become the first.

LOC_A=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 LocationId FROM dbo.Locations WHERE TenantId=1 ORDER BY CASE WHEN IsPrimary=1 THEN 0 ELSE 1 END, LocationId" | tr -d ' \r')
LOC_B=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 LocationId FROM dbo.Locations WHERE TenantId=1 AND LocationId<>$LOC_A ORDER BY LocationId" | tr -d ' \r')
EXP_A=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeCustomers WHERE LocationId=$LOC_A" | tr -d ' \r')
EXP_B=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeCustomers WHERE LocationId=$LOC_B" | tr -d ' \r')
EXP_ALL=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeCustomers WHERE TenantId=1" | tr -d ' \r')

chk "exactly one primary location per tenant" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MAX(n),0) FROM (SELECT COUNT(*) AS n FROM dbo.Locations WHERE IsPrimary=1 GROUP BY TenantId) x" | tr -d ' \r')" "1"

# The lesson from the reference product, checked against the live schema rather
# than the source: only the customer and the two inventory tables carry a
# LocationId. Everything else reaches its branch by join, so there is no second
# copy to drift to NULL.
chk "only three DME tables store a location" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id WHERE c.name='LocationId' AND t.name LIKE 'Dme%'" | tr -d ' \r')" "3"
chk "and none of the three is nullable" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id WHERE c.name='LocationId' AND t.name LIKE 'Dme%' AND c.is_nullable=1" | tr -d ' \r')" "0"

# Location must never become a security predicate: the all-branches roll-up
# would then need a hole punched through tenant isolation to work at all.
chk "location is not in the row level security policy" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.security_predicates sp JOIN sys.security_policies p ON p.object_id=sp.object_id CROSS APPLY (SELECT sp.predicate_definition AS d) x WHERE p.name='TenantIsolationPolicy' AND x.d LIKE '%LocationId%'" | tr -d ' \r')" "0"

# --- switching branch has to move the SERVER-RENDERED screens, not just the SPA
custcount() { curl -s -b $J "$BASE/Dme/Customers" | grep -oE 'LMS-[0-9]+' | sort -u | wc -l | tr -d ' '; }
switch() {
  curl -s -c $J -b $J -o /tmp/dme-switch.json -X POST $BASE/api/locations/switch \
    -H "Authorization: Bearer $1" -H 'Content-Type: application/json' -d "{\"LocationId\":$2}"
  grep -oE '"Token":"[^"]+' /tmp/dme-switch.json | sed 's/"Token":"//'
}

# CA_TOKEN comes from the sign in in section 2. Deliberately not a fresh sign
# in: that would be a third one in a single run, and the auth limiter allows
# five requests a minute.
if [ -z "$CA_TOKEN" ]; then
  no "no bearer token from the earlier sign in, so the branch switch cannot be exercised"
fi

T=$(switch "$CA_TOKEN" "$LOC_A")
chk "branch A shows only its own customers" "$(custcount)" "$EXP_A"
T=$(switch "$T" "$LOC_B")
chk "branch B shows only its own customers" "$(custcount)" "$EXP_B"
T=$(switch "$T" 0)
chk "all locations rolls the whole business back up" "$(custcount)" "$EXP_ALL"

# The point of doing it through the session cookie rather than a second cookie:
# the branch travels inside the SIGNED token, so it cannot be edited in a browser.
echo "$(grep -oE '"LocationName":"[^"]*"' /tmp/dme-switch.json)" | grep -q 'All locations' \
  && ok "the switch reports the all-branches selection back" \
  || no "the switch reports the all-branches selection back"

# --- the money follows the branch too
T=$(switch "$T" "$LOC_A")
MONTH_START=$(date +%Y-%m-01)
EXP_PAID_A=$($SQL -Q "SET NOCOUNT ON; SELECT FORMAT(ISNULL(SUM(PaidAmount),0),'N2') FROM dbo.vDmePaymentLines WHERE IsVoided=0 AND LocationId=$LOC_A AND PostedDate >= '$MONTH_START' AND PostedDate < DATEADD(month,1,'$MONTH_START')" | tr -d ' \r')
curl -s -b $J "$BASE/Dme/Dashboard" | grep -q "\$$EXP_PAID_A" \
  && ok "the amount paid tile is scoped to the branch ($EXP_PAID_A)" \
  || no "the amount paid tile is scoped to the branch (expected $EXP_PAID_A)"

# --- inventory is the deliberate exception: it owns its location
chk "stock is reported per branch" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(DISTINCT LocationId) FROM dbo.vDmeStockByLocation WHERE TenantId=1" | tr -d ' \r')" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(DISTINCT LocationId) FROM dbo.DmeStockMovements WHERE TenantId=1" | tr -d ' \r')"
curl -s -b $J "$BASE/Dme/Inventory" | grep -q 'Here' \
  && ok "the inventory screen shows what this branch holds" \
  || no "the inventory screen shows what this branch holds"

# --- a branch from another supplier cannot be used, even though RLS covers only TenantId
FOREIGN=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 LocationId FROM dbo.Locations WHERE TenantId<>1 ORDER BY LocationId" | tr -d ' \r')
if [ -n "$FOREIGN" ]; then
  NTOK=$(curl -s -b $J -c $J "$BASE/Dme/NewCustomer" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+"' | head -1 | sed 's/.*value="//;s/"//')
  curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreateCustomer \
    --data-urlencode "__RequestVerificationToken=$NTOK" \
    -d 'firstName=Cross' -d 'lastName=Tenant' -d "locationId=$FOREIGN" \
    -d 'insCopay=0' -d 'insCoins=0' -d 'insDeductible=0'
  chk "a branch belonging to another supplier is refused" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeCustomers WHERE LocationId=$FOREIGN" | tr -d ' \r')" "0"
  # And the switch endpoint refuses it too.
  curl -s -o /tmp/dme-foreign.json -X POST $BASE/api/locations/switch \
    -H "Authorization: Bearer $T" -H 'Content-Type: application/json' -d "{\"LocationId\":$FOREIGN}" > /dev/null
  grep -q '"Success":false' /tmp/dme-foreign.json \
    && ok "switching to another supplier's branch is refused" \
    || no "switching to another supplier's branch is refused"
fi

# Leave the session on all locations so later re-runs start from a known place.
T=$(switch "$T" 0)
rm -f /tmp/dme-switch.json /tmp/dme-foreign.json

echo "=============================================================="
echo " 12. USER TO LOCATION GRANTS"
echo "=============================================================="
# Branches became a real restriction here, not just a filter. The rule that
# matters is the empty one: a restricted user with no grants must see NOTHING,
# because the alternative hands the least configured account the widest access.

chk "every restricted user has at least one branch" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.Users u WHERE u.IsActive=1 AND u.TenantId IS NOT NULL AND u.Role >= 2 AND NOT EXISTS (SELECT 1 FROM dbo.UserLocations ul WHERE ul.UserId=u.UserId)" | tr -d ' \r')" "0"

chk "no grant points outside its user's tenant" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.UserLocations ul JOIN dbo.Users u ON u.UserId=ul.UserId JOIN dbo.Locations l ON l.LocationId=ul.LocationId WHERE l.TenantId <> u.TenantId" | tr -d ' \r')" "0"

chk "the same grant cannot be stored twice" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM (SELECT UserId, LocationId FROM dbo.UserLocations GROUP BY UserId, LocationId HAVING COUNT(*) > 1) x" | tr -d ' \r')" "0"

# --- a genuinely restricted user, end to end
RU_EMAIL=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 u.Email FROM dbo.Users u WHERE u.TenantId=1 AND u.Role >= 2 AND u.IsActive=1 AND (SELECT COUNT(*) FROM dbo.UserLocations ul WHERE ul.UserId=u.UserId) = 1 ORDER BY u.UserId" | tr -d ' \r')

if [ -z "$RU_EMAIL" ]; then
  echo "  INFO  no single-branch user on this database, skipping the restricted-user checks"
else
  RU_ID=$($SQL -Q "SET NOCOUNT ON; SELECT UserId FROM dbo.Users WHERE Email='$RU_EMAIL'" | tr -d ' \r')
  RU_LOC=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 LocationId FROM dbo.UserLocations WHERE UserId=$RU_ID" | tr -d ' \r')
  RU_LOCNAME=$($SQL -Q "SET NOCOUNT ON; SELECT Name FROM dbo.Locations WHERE LocationId=$RU_LOC" | tr -d '\r' | sed 's/ *$//')
  RU_EXPECT=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeCustomers WHERE LocationId=$RU_LOC" | tr -d ' \r')
  OTHER_LOC=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 LocationId FROM dbo.Locations WHERE TenantId=1 AND LocationId <> $RU_LOC ORDER BY LocationId" | tr -d ' \r')

  # Own cookie jar so the clinic admin session further up is left intact.
  RJ=restricted-cookies.txt
  rm -f $RJ
  ru_signin() {
    curl -s -c $RJ -o /dev/null -X POST $BASE/api/auth/login -H 'Content-Type: application/json' \
         -d "{\"Email\":\"$RU_EMAIL\",\"Password\":\"$RESTRICTED_PASSWORD\"}"
    RU_BODY=$(curl -s -c $RJ -b $RJ -X POST $BASE/api/auth/verify-otp -H 'Content-Type: application/json' \
         -d "{\"Email\":\"$RU_EMAIL\",\"OtpCode\":\"123456\"}")
    RU_TOKEN=$(echo "$RU_BODY" | grep -oE '"Token":"[^"]+' | sed 's/"Token":"//')
    [ -n "$RU_TOKEN" ]
  }

  if [ -z "$RESTRICTED_PASSWORD" ]; then
    echo "  SKIP  restricted-user sign in (set RESTRICTED_PASSWORD to include it)"
  elif ! ru_signin; then
    echo "  INFO  auth rate limit on the restricted sign in, waiting out the window and retrying once"
    sleep 61
    ru_signin
  fi

  if [ -n "$RU_TOKEN" ]; then
    # The branch they land on must be one they are granted. Before this was
    # fixed, login handed them the tenant's PRIMARY branch, which they had no
    # grant for, so the product was signed in and completely empty with nothing
    # on screen explaining why.
    echo "$RU_BODY" | grep -q "\"LocationName\":\"$RU_LOCNAME\"" \
      && ok "a restricted user lands on a branch they are granted ($RU_LOCNAME)" \
      || no "a restricted user lands on a branch they are granted (expected $RU_LOCNAME)"

    chk "and sees only that branch's customers" \
      "$(curl -s -b $RJ "$BASE/Dme/Customers" | grep -oE 'LMS-[0-9]+' | sort -u | wc -l | tr -d ' ')" "$RU_EXPECT"

    chk "the switcher offers only their branches" \
      "$(curl -s -H "Authorization: Bearer $RU_TOKEN" "$BASE/api/locations/dropdown" | grep -oc '"LocationId"')" "1"

    curl -s -o /tmp/dme-ru-switch.json -X POST $BASE/api/locations/switch \
      -H "Authorization: Bearer $RU_TOKEN" -H 'Content-Type: application/json' \
      -d "{\"LocationId\":$OTHER_LOC}" > /dev/null
    grep -q '"Success":false' /tmp/dme-ru-switch.json \
      && ok "switching to a branch they were not granted is refused" \
      || no "switching to a branch they were not granted is refused"

    # All-branches for a restricted user means all of THEIRS, never the tenant's.
    curl -s -o /dev/null -X POST $BASE/api/locations/switch \
      -H "Authorization: Bearer $RU_TOKEN" -H 'Content-Type: application/json' -d '{"LocationId":0}'
    chk "all locations still means only their branches" \
      "$(curl -s -b $RJ "$BASE/Dme/Customers" | grep -oE 'LMS-[0-9]+' | sort -u | wc -l | tr -d ' ')" "$RU_EXPECT"
  fi

  rm -f $RJ /tmp/dme-ru-switch.json
fi

echo
echo "13. PAYER CATALOG (Office Ally national list)"
echo "----------------------------------------------------------------"

# The client's complaint was that the insurance list was not exhaustive. It had
# four rows. These checks prove it is the vendor list, that it is GLOBAL rather
# than copied per tenant, and that the form cannot be talked into filing a payer
# that is not in it.

chk "the catalog holds the Office Ally list, not a demo seed" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT CASE WHEN COUNT(*) > 4000 THEN 'yes' ELSE 'no' END FROM dbo.DmePayers" | tr -d ' \r')" "yes"

chk "the catalog is not tenant scoped" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.DmePayers') AND name='TenantId'" | tr -d ' \r')" "0"

chk "the catalog is outside the row level security policy" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id=OBJECT_ID('dbo.DmePayers')" | tr -d ' \r')" "0"

# A payer name reaches a claim, so being found matters. The vendor spells it
# BCBS; the card in the customer's hand says Blue Cross. Counting MATCHES with
# grep -o | wc -l, not lines with grep -c: the response is one line, so a line
# count is 1 whether the search found everything or nothing.
BC_HITS=$(curl -s -b $J -c $J "$BASE/Lookups/Payers?q=blue%20cross%20of%20texas" | grep -o '"id"' | wc -l | tr -d ' ')
chk "a payer is found by the name printed on the card, not the vendor's spelling" \
  "$BC_HITS" "$(curl -s -b $J -c $J "$BASE/Lookups/Payers?q=bcbs%20texas" | grep -o '"id"' | wc -l | tr -d ' ')"

if [ "$BC_HITS" = "0" ]; then
  no "the Blue Cross search returned nothing at all, so the check above compared two zeroes"
fi

chk "a Payer ID pasted off a remittance finds its payer" \
  "$(curl -s -b $J -c $J "$BASE/Lookups/Payers?q=84980" | grep -o '"code":"84980"' | wc -l | tr -d ' ')" "1"

chk "a LIKE wildcard is not a way to dump the catalog" \
  "$(curl -s -b $J -c $J "$BASE/Lookups/Payers?q=%25" | tr -d ' \r')" "[]"

chk "payer search needs a session" \
  "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/Lookups/Payers?q=aetna")" "401"

# --- the server, not the browser, decides which payer is filed
#
# EVERY id below is derived from this HIGH WATER MARK, taken before anything is
# written. Nothing this section deletes can predate it.
#
# The first draft of this section took "the newest customer" as the one it had
# just created. When the POST failed, that was a SEEDED customer, and the
# cleanup deleted it. A check that tidies up after itself has to prove it made
# the mess first.
PC_BEFORE=$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MAX(CustomerId),0) FROM dbo.DmeCustomers" | tr -d ' \r')

PC_TOK=$(curl -s -b $J -c $J "$BASE/Dme/NewCustomer" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+' | head -1 | sed 's/.*value="//')
PC_LOC=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 LocationId FROM dbo.Locations WHERE TenantId=1 AND IsActive=1 ORDER BY IsPrimary DESC" | tr -d ' \r')
PC_REAL=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 PayerId FROM dbo.DmePayers WHERE PayerCode='84980'" | tr -d ' \r')

curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreateCustomer \
  --data-urlencode "__RequestVerificationToken=$PC_TOK" \
  -d 'firstName=PayerCat' -d 'lastName=Check' -d 'dob=03/04/1950' -d "locationId=$PC_LOC" \
  -d "insPayerId=$PC_REAL" -d 'insCopay=0' -d 'insCoins=0' -d 'insDeductible=0'

PC_CUST=$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MIN(CustomerId),0) FROM dbo.DmeCustomers WHERE CustomerId > $PC_BEFORE" | tr -d ' \r')

if [ "$PC_CUST" = "0" ]; then
  no "the payer catalog check could not create a customer, so the rest of it is unproven"
else
  chk "the payer name and Payer ID are read from the catalog, not posted" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT PayerName + '|' + PayerId FROM dbo.DmeCustomerInsurances WHERE CustomerId=$PC_CUST" | tr -d '\r' | sed 's/ *$//')" "BCBS Texas (HCSC)|84980"

  # A pasted date of birth has to survive the round trip. The stored value is
  # ciphertext, so the age the screen computes from it is what can be read back.
  # 10# forces base ten: bash reads a leading-zero date like 0827 as octal and
  # dies on it.
  PC_AGE=$(( $(date +%Y) - 1950 ))
  [ "$((10#$(date +%m%d)))" -lt 304 ] && PC_AGE=$((PC_AGE - 1))
  chk "a pasted date of birth is stored as the date that was pasted" \
    "$(curl -s -b $J -c $J "$BASE/Dme/Customer/$PC_CUST" | grep -oE '· [0-9]+ yrs' | head -1 | grep -oE '[0-9]+')" "$PC_AGE"

  # --- a payer that is not in the catalog cannot be filed, however it is posted
  curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreateCustomer \
    --data-urlencode "__RequestVerificationToken=$PC_TOK" \
    -d 'firstName=Forged' -d 'lastName=Payer' -d "locationId=$PC_LOC" \
    -d 'insPayerId=999999' -d 'insCopay=0' -d 'insCoins=0' -d 'insDeductible=0'

  PC_FORGED=$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MAX(CustomerId),0) FROM dbo.DmeCustomers WHERE CustomerId > $PC_CUST" | tr -d ' \r')

  if [ "$PC_FORGED" = "0" ]; then
    no "the forged-payer POST created no customer, so the insurance check is unproven"
  else
    chk "a payer id that is not in the catalog files no insurance at all" \
      "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeCustomerInsurances WHERE CustomerId=$PC_FORGED" | tr -d ' \r')" "0"
  fi
fi

# --- clean up ONLY what this section created, so it is safe to re-run
$SQL -Q "SET NOCOUNT ON; EXEC sp_set_session_context N'CurrentTenantId', 1;
         DELETE FROM dbo.DmeCustomerInsurances   WHERE CustomerId > $PC_BEFORE;
         DELETE FROM dbo.DmeCustomerSearchTokens WHERE CustomerId > $PC_BEFORE;
         DELETE FROM dbo.DmeCustomers            WHERE CustomerId > $PC_BEFORE;" > /dev/null

chk "the payer catalog checks cleaned up after themselves" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeCustomers WHERE CustomerId > $PC_BEFORE" | tr -d ' \r')" "0"

echo
echo "14. CODE CATALOGS (CMS ICD-10-CM and HCPCS Level II)"
echo "----------------------------------------------------------------"

# The client said three lists were "not exhaustive". Payers is section 13; these
# are the other two. All three are global reference data, never copied per
# tenant, and in every case the SERVER decides what gets filed, not the browser.

chk "the ICD catalog is the whole CMS code set" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT CASE WHEN COUNT(*) > 70000 THEN 'yes' ELSE 'no' END FROM dbo.IcdCodes" | tr -d ' \r')" "yes"

chk "the code the client asked about is in it" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.IcdCodes WHERE Code='E66.9'" | tr -d ' \r')" "1"

# CMS ships header codes like E66 alongside billable leaves like E66.9, and a
# header on a claim is a denial. The codes file holds only the billable set.
chk "non-billable header codes were not loaded" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.IcdCodes WHERE Code='E66'" | tr -d ' \r')" "0"

chk "the HCPCS national list is loaded" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT CASE WHEN COUNT(*) > 8000 THEN 'yes' ELSE 'no' END FROM dbo.HcpcsNationalCodes" | tr -d ' \r')" "yes"

chk "no CPT code slipped in with it" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.HcpcsNationalCodes WHERE Code NOT LIKE '[A-Z]%'" | tr -d ' \r')" "0"

for T in IcdCodes HcpcsNationalCodes; do
  chk "$T is not tenant scoped" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.$T') AND name='TenantId'" | tr -d ' \r')" "0"
  chk "$T is outside the row level security policy" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id=OBJECT_ID('dbo.$T')" | tr -d ' \r')" "0"
done

# The supplier's item master is the OTHER table, and it stays tenant data: it
# holds their prices. Pouring 8,623 national codes into it was the mistake this
# design avoids.
chk "the supplier's item master is still tenant scoped" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.HcpcsCodes') AND name='TenantId'" | tr -d ' \r')" "1"

chk "every item this supplier stocks names a real HCPCS code" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.HcpcsCodes c WHERE NOT EXISTS (SELECT 1 FROM dbo.HcpcsNationalCodes n WHERE n.Code=c.Hcpcs)" | tr -d ' \r')" "0"

# --- the searches answer the way a human types
#
# A count rather than an exact number: the point is that searching a condition
# returns a real set of choices, which the twelve hardcoded codes never could.
DX_HITS=$(curl -s -b $J -c $J "$BASE/Lookups/Icd?q=obesity" | grep -o '"code"' | wc -l | tr -d ' ')
if [ "$DX_HITS" -ge 10 ]; then
  ok "a diagnosis is found by condition, not only by code ($DX_HITS matches)"
else
  no "a diagnosis is found by condition, not only by code (only $DX_HITS matches)"
fi

for SPELLING in "E66.9" "E669"; do
  chk "the code $SPELLING resolves whichever way it is typed" \
    "$(curl -s -b $J -c $J "$BASE/Lookups/Icd?q=$SPELLING" | grep -o '"code":"E66.9"' | wc -l | tr -d ' ')" "1"
done

chk "a HCPCS code is found by what the thing is" \
  "$(curl -s -b $J -c $J "$BASE/Lookups/Hcpcs?q=oxygen%20concentrator" | grep -o '"code":"E1390"' | wc -l | tr -d ' ')" "1"

for EP in Icd Hcpcs; do
  chk "the $EP lookup needs a session" \
    "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/Lookups/$EP?q=test")" "401"
done

# --- a diagnosis is resolved on the server, never taken from the browser
CC_BEFORE=$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MAX(CustomerId),0) FROM dbo.DmeCustomers" | tr -d ' \r')
CC_TOK=$(curl -s -b $J -c $J "$BASE/Dme/NewCustomer" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+' | head -1 | sed 's/.*value="//')
CC_LOC=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 LocationId FROM dbo.Locations WHERE TenantId=1 AND IsActive=1 ORDER BY IsPrimary DESC" | tr -d ' \r')

dx_post() {
  curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreateCustomer \
    --data-urlencode "__RequestVerificationToken=$CC_TOK" \
    -d "firstName=Dx" -d "lastName=$1" -d "locationId=$CC_LOC" -d "dxCode=$2" \
    -d 'insPayerId=0' -d 'insCopay=0' -d 'insCoins=0' -d 'insDeductible=0'
}

dx_post Undotted "E669"
dx_post Bogus "ZZ99.9"

CC_FIRST=$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MIN(CustomerId),0) FROM dbo.DmeCustomers WHERE CustomerId > $CC_BEFORE" | tr -d ' \r')

if [ "$CC_FIRST" = "0" ]; then
  no "the diagnosis checks could not create a customer, so the rest of them are unproven"
else
  # Posted undotted, filed dotted, with the CMS wording attached.
  chk "an undotted code is filed as the catalog spells it, with its description" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT IcdCode + '|' + Description FROM dbo.DmeCustomerDiagnoses WHERE CustomerId=$CC_FIRST" | tr -d '\r' | sed 's/ *$//')" "E66.9|Obesity, unspecified"

  CC_BOGUS=$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MAX(CustomerId),0) FROM dbo.DmeCustomers WHERE CustomerId > $CC_FIRST" | tr -d ' \r')
  chk "a code that is not valid ICD-10-CM files no diagnosis at all" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeCustomerDiagnoses WHERE CustomerId=$CC_BOGUS" | tr -d ' \r')" "0"
fi

# --- an item cannot be added to the catalog under a code that cannot be billed
HC_BEFORE=$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.HcpcsCodes" | tr -d ' \r')
HC_TOK=$(curl -s -b $J -c $J "$BASE/Hcpcs" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+' | head -1 | sed 's/.*value="//')
HC_RETIRED=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 Code FROM dbo.HcpcsNationalCodes WHERE TerminatedOn IS NOT NULL AND TerminatedOn < GETDATE() ORDER BY Code" | tr -d ' \r')
HC_NEW=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 n.Code FROM dbo.HcpcsNationalCodes n WHERE n.TerminatedOn IS NULL AND NOT EXISTS (SELECT 1 FROM dbo.HcpcsCodes c WHERE c.Hcpcs=n.Code) ORDER BY n.Code" | tr -d ' \r')

add_item() {
  curl -s -b $J -c $J -o /dev/null -X POST $BASE/Hcpcs/AddItem \
    --data-urlencode "__RequestVerificationToken=$HC_TOK" \
    -d "hcpcs=$1" -d 'purchasePrice=10' -d 'monthlyRate=0' -d 'cappedRentalMonths=0' -d 'reorderPoint=0'
}

add_item "ZZZZZ"
add_item "$HC_RETIRED"

chk "an invented HCPCS code adds nothing" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.HcpcsCodes" | tr -d ' \r')" "$HC_BEFORE"

add_item "$HC_NEW"
chk "a real, current code CAN be added" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.HcpcsCodes WHERE Hcpcs='$HC_NEW'" | tr -d ' \r')" "1"

add_item "$HC_NEW"
chk "adding the same code twice does not give one item two prices" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.HcpcsCodes WHERE Hcpcs='$HC_NEW'" | tr -d ' \r')" "1"

# --- clean up ONLY what this section created
$SQL -Q "SET NOCOUNT ON; EXEC sp_set_session_context N'CurrentTenantId', 1;
         DELETE FROM dbo.DmeCustomerDiagnoses    WHERE CustomerId > $CC_BEFORE;
         DELETE FROM dbo.DmeCustomerInsurances   WHERE CustomerId > $CC_BEFORE;
         DELETE FROM dbo.DmeCustomerSearchTokens WHERE CustomerId > $CC_BEFORE;
         DELETE FROM dbo.DmeCustomers            WHERE CustomerId > $CC_BEFORE;
         DELETE FROM dbo.HcpcsCodes              WHERE Hcpcs = '$HC_NEW';" > /dev/null

chk "the code catalog checks cleaned up after themselves" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT (SELECT COUNT(*) FROM dbo.DmeCustomers WHERE CustomerId > $CC_BEFORE) + (SELECT COUNT(*) FROM dbo.HcpcsCodes) - $HC_BEFORE" | tr -d ' \r')" "0"

echo
echo "15. DROP SHIPPING (items that never enter the warehouse)"
echo "----------------------------------------------------------------"

# The client: "Most of the items we deliver are drop-shipped from
# manufacturer/distributors." The guard that matters is an ABSENCE: such a line
# must write no stock movement and no serialised unit, or on-hand marches
# negative for the majority of what they sell. It must still be billed.
#
# Four tables in this database carry filtered indexes, which makes DML against
# them require QUOTED_IDENTIFIER ON. sqlcmd defaults it OFF, so the writes in
# this section go through SQLW, not SQL.
SQLW="$SQL -I"

chk "distributors are tenant scoped" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.DmeDistributors') AND name='TenantId'" | tr -d ' \r')" "1"

chk "distributors are inside the row level security policy" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.security_predicates WHERE target_object_id=OBJECT_ID('dbo.DmeDistributors')" | tr -d ' \r')" "3"

chk "drop-shipped is derived, not stored" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.DmeOrderLines') AND name LIKE '%DropShip%'" | tr -d ' \r')" "0"

chk "the order line index is not filtered, so the table stays writable from plain sqlcmd" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MAX(CAST(has_filter AS INT)),0) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.DmeOrderLines') AND name='IX_DmeOrderLines_Distributor'" | tr -d ' \r')" "0"

# --- a distributor, then a MIXED order: one line direct, one out of stock
DS_TOK=$(curl -s -b $J -c $J "$BASE/Dme/Distributors" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+' | head -1 | sed 's/.*value="//')
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/AddDistributor \
  --data-urlencode "__RequestVerificationToken=$DS_TOK" -d 'name=VerifyScriptDistributor' -d 'accountNo=VS-1'
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/AddDistributor \
  --data-urlencode "__RequestVerificationToken=$DS_TOK" -d 'name=VerifyScriptDistributor'

chk "the same distributor cannot be added twice" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeDistributors WHERE Name='VerifyScriptDistributor'" | tr -d ' \r')" "1"

DS_ID=$($SQL -Q "SET NOCOUNT ON; SELECT DistributorId FROM dbo.DmeDistributors WHERE Name='VerifyScriptDistributor'" | tr -d ' \r')
DS_CUST=$($SQL -Q "SET NOCOUNT ON; SELECT TOP 1 CustomerId FROM dbo.DmeCustomers WHERE LocationId=1 ORDER BY CustomerId" | tr -d ' \r')
DS_ORD_BEFORE=$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MAX(OrderId),0) FROM dbo.DmeOrders" | tr -d ' \r')
DS_STOCK_BEFORE=$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(SUM(Qty),0) FROM dbo.DmeStockMovements WHERE Hcpcs='E0260'" | tr -d ' \r')

DS_OTOK=$(curl -s -b $J -c $J "$BASE/Dme/NewOrder" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+' | head -1 | sed 's/.*value="//')
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/CreateOrder \
  --data-urlencode "__RequestVerificationToken=$DS_OTOK" -d "customerId=$DS_CUST" -d 'deposit=0' \
  -d 'hcpcs=E0260' -d 'mode=rental'   -d 'qty=1' -d "distributorId=$DS_ID" -d 'distributorRef=VS-TRACK-1' \
  -d 'hcpcs=E0114' -d 'mode=purchase' -d 'qty=2' -d 'distributorId=0'      -d 'distributorRef='

DS_ORD=$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(MAX(OrderId),0) FROM dbo.DmeOrders WHERE OrderId > $DS_ORD_BEFORE" | tr -d ' \r')

if [ "$DS_ORD" = "0" ]; then
  no "the drop-ship check could not create an order, so the rest of it is unproven"
else
  chk "the drop-shipped line records its distributor and reference" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT CAST(DistributorId AS VARCHAR) + '|' + DistributorRef FROM dbo.DmeOrderLines WHERE OrderId=$DS_ORD AND Hcpcs='E0260'" | tr -d '\r' | sed 's/ *$//')" "$DS_ID|VS-TRACK-1"

  chk "the line taken from stock records no distributor" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeOrderLines WHERE OrderId=$DS_ORD AND Hcpcs='E0114' AND DistributorId IS NOT NULL" | tr -d ' \r')" "0"

  # --- deliver it, and watch what is NOT written
  DS_DTOK=$(curl -s -b $J -c $J "$BASE/Dme/Order/$DS_ORD" | grep -oE 'name="__RequestVerificationToken"[^>]*value="[^"]+' | head -1 | sed 's/.*value="//')
  curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/Deliver \
    --data-urlencode "__RequestVerificationToken=$DS_DTOK" -d "id=$DS_ORD" -d 'signedBy=Verify Script'

  chk "the drop-shipped item moved no stock" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeStockMovements WHERE RefType='DmeOrder' AND RefId=$DS_ORD AND Hcpcs='E0260'" | tr -d ' \r')" "0"

  chk "its on-hand is exactly where it was" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(SUM(Qty),0) FROM dbo.DmeStockMovements WHERE Hcpcs='E0260'" | tr -d ' \r')" "$DS_STOCK_BEFORE"

  chk "no unit of ours was registered for something we never held" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeSerializedUnits WHERE Hcpcs='E0260' AND CustomerId=$DS_CUST AND InServiceDate >= CAST(GETDATE() AS DATE)" | tr -d ' \r')" "0"

  # The other half. If the guard drifted upwards it would silently stop billing
  # the majority of this supplier's business.
  chk "the drop-shipped item was still BILLED" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeClaimLines cl JOIN dbo.DmeClaims c ON c.ClaimId=cl.ClaimId WHERE c.OrderId=$DS_ORD AND cl.Hcpcs='E0260'" | tr -d ' \r')" "1"

  chk "the line from stock DID move stock, so the guard is not just switched off" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT ISNULL(SUM(Qty),0) FROM dbo.DmeStockMovements WHERE RefType='DmeOrder' AND RefId=$DS_ORD AND Hcpcs='E0114'" | tr -d ' \r')" "-2"

  chk "it appears on the drop-shipment view, marked arrived" \
    "$($SQL -Q "SET NOCOUNT ON; SELECT CAST(HasArrived AS INT) FROM dbo.vDmeDropShipments WHERE OrderId=$DS_ORD" | tr -d ' \r')" "1"

  curl -s -b $J -c $J "$BASE/Dme/Inventory" | grep -q 'VS-TRACK-1' \
    && ok "the inventory screen shows what is coming direct from a distributor" \
    || no "the inventory screen shows what is coming direct from a distributor"
fi

# --- a distributor is retired, never deleted
curl -s -b $J -c $J -o /dev/null -X POST $BASE/Dme/RetireDistributor \
  --data-urlencode "__RequestVerificationToken=$DS_TOK" -d "id=$DS_ID"

chk "retiring a distributor keeps the row" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.DmeDistributors WHERE DistributorId=$DS_ID AND RetiredAt IS NOT NULL" | tr -d ' \r')" "1"

curl -s -b $J -c $J "$BASE/Dme/NewOrder" | grep -q 'VerifyScriptDistributor' \
  && no "a retired distributor is still offered on a new order" \
  || ok "a retired distributor is no longer offered on a new order"

# --- clean up ONLY what this section created
$SQLW -Q "SET NOCOUNT ON; EXEC sp_set_session_context N'CurrentTenantId', 1;
          DECLARE @c INT = (SELECT ClaimId FROM dbo.DmeClaims WHERE OrderId = $DS_ORD);
          DELETE FROM dbo.DmeClaimLines      WHERE ClaimId = @c;
          DELETE FROM dbo.DmeClaims          WHERE ClaimId = @c;
          DELETE FROM dbo.DmeRentals         WHERE OrderId = $DS_ORD;
          DELETE FROM dbo.DmeStockMovements  WHERE RefType='DmeOrder' AND RefId = $DS_ORD;
          DELETE FROM dbo.DmeSerializedUnits WHERE CustomerId=$DS_CUST AND InServiceDate >= CAST(GETDATE() AS DATE);
          DELETE FROM dbo.DmeOrderLines      WHERE OrderId = $DS_ORD;
          DELETE FROM dbo.DmeOrders          WHERE OrderId = $DS_ORD;
          DELETE FROM dbo.DmeDistributors    WHERE DistributorId = $DS_ID;" > /dev/null

chk "the drop-ship checks cleaned up after themselves" \
  "$($SQL -Q "SET NOCOUNT ON; SELECT (SELECT COUNT(*) FROM dbo.DmeOrders WHERE OrderId > $DS_ORD_BEFORE) + (SELECT COUNT(*) FROM dbo.DmeDistributors WHERE DistributorId=$DS_ID)" | tr -d ' \r')" "0"

echo
echo "=============================================================="
printf " RESULT: %d passed, %d failed\n" $pass $fail
echo "=============================================================="
[ $fail -eq 0 ]
