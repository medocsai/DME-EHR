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
#
# Usage:
#   cd ehr-system && dotnet run --urls http://localhost:5077
#   bash scripts/verify-dme-foundation.sh
#
# Requires sqlcmd and curl. Safe to re-run: the only state it changes is one
# claim status, which it restores.
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
curl -s -c $J -X POST $BASE/api/auth/login -H 'Content-Type: application/json' \
     -d '{"Email":"admin@md.com","Password":"DemoPass@2026"}' > /dev/null
HDRS=$(curl -s -c $J -b $J -D - -o /dev/null -X POST $BASE/api/auth/verify-otp \
     -H 'Content-Type: application/json' -d '{"Email":"admin@md.com","OtpCode":"123456"}')
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
printf " RESULT: %d passed, %d failed\n" $pass $fail
echo "=============================================================="
[ $fail -eq 0 ]
