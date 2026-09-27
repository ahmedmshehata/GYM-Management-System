using Microsoft.Data.Sqlite;

namespace GymPro.Tests;

/// <summary>
/// Builds SQLite files with the legacy JASystem / DoctorGYM schema (roles, members, subscribe, Attendance),
/// so the import tests are self-contained and no real member data lives in the repository.
/// Column names, types and quirks (misspelled "Hieght"/"Wieght", Base64 photos in ImagePath,
/// dates as TEXT) match the original db_at.db.
/// </summary>
public static class LegacyFixture
{
    private const string Schema = """
        CREATE TABLE `roles` (`role_id` INT, `role_name` TEXT, `role_count` INT, `role_type` TEXT, `role_amount` REAL, PRIMARY KEY(`role_id`));
        CREATE TABLE `members` (`ID` INT, `FullName` TEXT, `n_ID` TEXT, `Phone` TEXT, `Job` TEXT, `Hieght` REAL, `Wieght` REAL,
                                `MemberAddress` TEXT, `ImagePath` TEXT, `Gender` TEXT, PRIMARY KEY(`ID`));
        CREATE TABLE `subscribe` (`subscribe_id` INT, `subscribe_mem` INT, `subscribe_role` INT, `subscribe_pay` REAL, `subscribe_rmind` REAL,
                                  `subscribe_f` DATE, `subscribe_t` DATE, `subscribe_flag` TEXT, PRIMARY KEY(`subscribe_id`));
        CREATE TABLE `Attendance` (`Attend_ID` INT, `MemberID` INT, `SubscribeID` INT, `AttendDate` DATE, PRIMARY KEY(`Attend_ID`));
        """;

    /// <summary>The 10 plans shipped with the old app (v2/db_at.db): session packs ("C") and monthly plans ("M").</summary>
    private static readonly (int Id, string Name, int Count, string Type)[] Roles =
    [
        (1, "12 حصة حديد", 12, "C"), (2, "12 حصة Fitness", 12, "C"),
        (3, "شهر حديد", 1, "M"), (4, "شهر Fitness", 1, "M"),
        (5, "ريع سنوي حديد ", 3, "M"), (6, "ربع سنوي Fitness", 3, "M"),
        (7, "نص سنوي حديد", 6, "M"), (8, "نص سنوي Fitness", 6, "M"),
        (9, "سنوي حديد ", 12, "M"), (10, "سنوي Fitness ", 12, "M"),
    ];

    /// <summary>Only the plans, like the empty v2 install.</summary>
    public static string CreatePlansOnly() => Create(withMember: false);

    /// <summary>
    /// Plans plus one member with a Base64 photo, one subscription and two check-ins on the same day
    /// (the old app never blocked double check-ins).
    /// </summary>
    public static string CreateWithMember() => Create(withMember: true);

    private static string Create(bool withMember)
    {
        var path = Path.Combine(Path.GetTempPath(), $"legacy-{Guid.NewGuid():N}.db");
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        conn.Open();
        Exec(conn, Schema);

        foreach (var r in Roles)
        {
            Exec(conn, "INSERT INTO roles VALUES ($id, $name, $count, $type, 0)",
                ("$id", r.Id), ("$name", r.Name), ("$count", r.Count), ("$type", r.Type));
        }

        if (withMember)
        {
            Exec(conn, "INSERT INTO members VALUES (1, 'Ahmed', '4545', '545', 'it', NULL, NULL, 'cairo', $photo, 'M')", ("$photo", FakeBase64Photo()));
            Exec(conn, "INSERT INTO subscribe VALUES (1, 1, 4, 200, 0, '2017-01-20', '2017-02-20', 'T')");
            Exec(conn, "INSERT INTO Attendance VALUES (1, 1, 1, '2017-02-01 00:00:00')");
            Exec(conn, "INSERT INTO Attendance VALUES (2, 1, 1, '2017-02-01 00:00:00')"); // same-day duplicate
        }

        return path;
    }

    /// <summary>JPEG-signed bytes, Base64-encoded with CRLF line breaks every 76 chars like the old app stored them.</summary>
    private static string FakeBase64Photo()
    {
        var bytes = new byte[600];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        for (var i = 2; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i * 7);
        }

        return Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks);
    }

    private static void Exec(SqliteConnection conn, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        cmd.ExecuteNonQuery();
    }
}
