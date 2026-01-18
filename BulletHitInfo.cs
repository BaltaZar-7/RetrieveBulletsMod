#nullable disable

using System;
using Il2Cpp;

namespace RetrieveBullets
{
    [Serializable]
    public class BulletHitInfo
    {
        public int Count;
        public float CreatedHours;

        public BulletHitInfo()
        {
            Count = 0;
            try
            {
                TimeOfDay tod = GameManager.GetTimeOfDayComponent();
                if (tod != null)
                {
                    CreatedHours = tod.GetHoursPlayedNotPaused();
                }
                else
                {
                    CreatedHours = 0f;
                }
            }
            catch
            {
                CreatedHours = 0f;
            }
        }
    }
}