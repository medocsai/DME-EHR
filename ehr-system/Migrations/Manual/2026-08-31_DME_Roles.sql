/* ============================================================================
   Five roles, and only five.

   The list came across whole from the clinical product: Clinician, Front Desk,
   Read Only, Medical Assistant, Nurse. A DME supplier has none of those. It has
   somebody who takes the order, somebody who delivers, somebody who bills, and
   an owner.

   The NUMBERS do not move. Twenty eight [Authorize] attributes name them and
   twenty eight live accounts carry them, so renumbering to tidy the list would
   silently change what people can do. Only the words changed:

       0  Super Admin   (unchanged)
       1  Clinic Admin  ->  Admin
       2  Clinician     ->  Intake
       3  Front Desk    ->  Delivery
       4  Biller        (unchanged)

   What this script fixes is the rows that were never in the list at all.

   Roles 6 and 7 were Medical Assistant and Nurse. Neither was ever added to
   GetRoleName, so thirteen accounts have been displaying as "Read Only" while
   being something else. That is worse than a wrong label: the screen was
   answering a question about permissions with a guess.

   They move to 3 (Delivery), and this grants nothing. 3, 5, 6 and 7 appear in
   NO [Authorize] attribute anywhere in the product, so all four carry exactly
   the same authority today: none beyond being signed in. The move makes the
   label true; it does not hand anybody a key.

   Super Admins (0) are untouched, as are Admins (1), Intake (2) and Billers (4).

   Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* Filtered indexes on dbo.Users make every write here require
   QUOTED_IDENTIFIER ON. sqlcmd defaults it OFF, so this script must be run
   with -I or it fails naming neither the index nor the reason. */

DECLARE @moved TABLE (UserId INT, OldRole INT, Email NVARCHAR(256));

UPDATE u
   SET Role = 3
OUTPUT inserted.UserId, deleted.Role, inserted.Email INTO @moved
  FROM dbo.Users u
 WHERE u.Role NOT IN (0, 1, 2, 3, 4);

SELECT 'Moved to Delivery (3)' AS Change, OldRole, Email FROM @moved ORDER BY OldRole, Email;

SELECT 'Roles now in use' AS Summary,
       Role,
       CASE Role WHEN 0 THEN 'Super Admin'
                 WHEN 1 THEN 'Admin'
                 WHEN 2 THEN 'Intake'
                 WHEN 3 THEN 'Delivery'
                 WHEN 4 THEN 'Biller'
                 ELSE 'UNKNOWN - should not exist' END AS Name,
       COUNT(*) AS Users
  FROM dbo.Users
 GROUP BY Role
 ORDER BY Role;
GO
