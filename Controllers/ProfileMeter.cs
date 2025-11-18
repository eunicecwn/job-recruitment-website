// Models/ProfileMeter.cs
namespace JobRecruitment.Models
{
    public static class ProfileMeter
    {
        public static int Compute(
            JobSeeker js,
            int skillCount,
            int expCount,
            int eduCount,
            int languageCount,
            int licenseCount)
        {
            int score = 0;
            void add(bool ok, int pts) { if (ok) score += pts; }

            add(!string.IsNullOrWhiteSpace(js.FullName), 10);
            add(!string.IsNullOrWhiteSpace(js.Email) && js.IsEmailVerified, 5);
            add(!string.IsNullOrWhiteSpace(js.Phone), 5);
            add(!string.IsNullOrWhiteSpace(js.Address), 5);
            add(!string.IsNullOrWhiteSpace(js.ExperienceLevel), 5);

            add(!string.IsNullOrWhiteSpace(js.ProfilePhotoFileName), 10);
            add(!string.IsNullOrWhiteSpace(js.ResumeFileName), 10);

            add(!string.IsNullOrWhiteSpace(js.Summary), 10);

            add(skillCount > 0, 8);
            add(expCount > 0, 8);
            add(eduCount > 0, 8);
            add(languageCount > 0, 8);
            add(licenseCount > 0, 8);

            return Math.Clamp(score, 0, 100);
        }
    }
}
