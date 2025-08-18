using SQLite;

namespace AmaScan.Classes
{
    public static class SQLiteExtensions
         {
        public static void RunInTransaction(this SQLiteConnection conn, Action action)
        {
            conn.BeginTransaction();
            try
            {
                action();
                conn.Commit();
            }
            catch
            {
                conn.Rollback();
                throw;
            }
        }
    }
}
