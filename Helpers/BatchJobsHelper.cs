using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

public static class BatchJobsHelper
{
    public static (int Pending, int Running, int Completed, int Failed) GroupByState(
        IEnumerable<Session> sessions)
    {
        int pending = 0, running = 0, completed = 0, failed = 0;
        foreach (var s in sessions)
        {
            switch (s.Status)
            {
                case "Pending":
                    pending++;
                    break;
                case "Running":
                    running++;
                    break;
                case "Succeeded":
                case "Completed":
                    completed++;
                    break;
                case "Failed":
                case "Error":
                    failed++;
                    break;
            }
        }
        return (pending, running, completed, failed);
    }
}
