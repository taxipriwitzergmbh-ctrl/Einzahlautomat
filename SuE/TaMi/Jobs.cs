using SuE.Tools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SuE.TaMi
{
    /*
        PROJEKT           :  Jobs
        VERSION           :  1.01
        
        ERSTELLUNGS-DATUM :  17.10.2018
        ÄNDERUNGS-DATUM   :  17.10.2018
        ÄNDERUNG:            siehe Entwicklungsgeschichte
        DURCHGEFUEHRT VON :  SuE-Software  [sue]

        FUNKTION          :  Liste mit TaMi Aufträgen

        BEMERKUNG         :  ---

        ======================== ENTWICKLUNGS-GESCHICHTE ========================

        --- Version 1.00 --------------------------------------------------------

        17.10.2018  mcs   Beginn der Implementation in NET
    */

    public class Jobs : IEnumerable<Job>
    {
        #region "Variables"

        private TimeSpan mTimeRangePast;
        private TimeSpan mTimeRangeFuture;

        private Dictionary<string, Job> mJobs;

        #endregion

        public Jobs()
        {
            mTimeRangePast = new TimeSpan(0, 4320, 0);
            mTimeRangeFuture = new TimeSpan(0, 43200, 0);
            mJobs = new Dictionary<string, Job>();
        }

        public int Count  
        {
            get { return mJobs.Count(); }
        }

        public Job this[string jobid]
        {
            get
            {
                Job job;
                lock (mJobs)
                {
                    mJobs.TryGetValue(jobid, out job);
                }
                return job;
            }
        }


        TaMiClient tamiClient;
        public TaMiClient TaMiClient
        {
            get
            {
                return tamiClient;
            }
            internal set
            {
                tamiClient = value;
            }
        }

        
        public List<Job> ToList()
        {
            List<Job> result;
            lock (mJobs)
            {
                result = mJobs.Values.ToList();
            }
            return result;
        }

        #region "IEnumerable<Job>"

        public IEnumerator<Job> GetEnumerator()
        {
            Dictionary<string, Job>.ValueCollection.Enumerator enumerator;

            lock (mJobs)
            {
                enumerator = mJobs.Values.GetEnumerator(); 
            }
            return enumerator; // mJobsDispatched.Values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion

        internal void SyncJob(Job job)
        {
            if (job == null) return;

            DateTime now = DateTime.Now;

            DateTime zeitFahrtMin = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                     zeitFahrtMin = zeitFahrtMin.Subtract(mTimeRangePast);

            DateTime zeitFahrtMax = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 59);
                     zeitFahrtMax = zeitFahrtMax.Add(mTimeRangeFuture);

            Job jobExisting;
            lock (mJobs)
            {
                mJobs.TryGetValue(job.JobId, out jobExisting);

                //Prüfe ob der neue Job noch in die Liste passt bzw. ob dieser entfernt werden muss
                //Alle offenen Aufträge bis ZeitFahrtMax
                //Alle vermittelten Aufträge
                //Alle Erfolgreich/Fehlfahrt/Storniert zwischen ZeitFahrtMin und ZeitFahrtMax
                if ((job.Status == JobStatus.OFFEN && job.ZeitFahrt > zeitFahrtMax) || ((job.Status != JobStatus.VERMITTELT && job.Status != JobStatus.OFFEN) && (job.ZeitFahrt < zeitFahrtMin || job.ZeitFahrt > zeitFahrtMax)))
                {
                    if (jobExisting != null)
                    {
                        Debug.WriteLine("CJOBS : SyncJob '{0}' REMOVE", job.JobId, job.ZeitFahrt);
                        mJobs.Remove(job.JobId);

                        return;
                    }
                    else
                    {
                        Debug.WriteLine("CJOBS : SyncJob '{0}' IGNORE", job.JobId, job.ZeitFahrt);
                        return;
                    }
                }

                //TODO: Den Job nach ZeitFahrt einfügen
                mJobs[job.JobId] = job;

                if (jobExisting != null)
                {
                    Debug.WriteLine("CJOBS : SyncJob '{0}' UPDATE", job.JobId, job.ZeitFahrt);
                }
                else
                {
                    Debug.WriteLine("CJOBS : SyncJob '{0}' ADD", job.JobId, job.ZeitFahrt);
                }

            } //lock (mJobsDispatched)
        }

        //Entfernt alle Jobs mit entsprechender Job.RelTyp, Job.RelId und dem Job.StatusId
        internal List<Job> RemoveByRelId(JobRelTyp relTyp, string relId, JobStatus jobStatus)
        {
            Job job;
            List<Job> jobsToRemove = new List<Job>();

            lock (mJobs)
            {

                //Suche die Jobs die entfernt werden müssen
                foreach (var pair in mJobs)
                {
                    job = pair.Value;

                    if (job.RelTyp == relTyp && job.Status == jobStatus && job.RelId.Equals(relId))
                    {
                        jobsToRemove.Add(job);
                    }
                }

                //Entferne diese jetzt aus mJobsDispatched
                foreach (Job jobToRemove in jobsToRemove)
                {
                    Debug.WriteLine("CJOBS : RemoveByRelId '{0}' REMOVE", jobToRemove);
                    mJobs.Remove(jobToRemove.JobId);

                }

            } //lock (mJobsDispatched)

            return jobsToRemove;
        }

        /// <summary>
        /// Synchronisiert die Jobs mit dem Datenpaket aus einer TAC.JOBMSG
        /// </summary>
        internal List<Job> SyncFromMessagePacket(MessagePacket packet, out SyncFlags nSyncFlags, out List<string> JobsRemoved)
        {
            short       bSyncVersion = packet.getByteS();
                        nSyncFlags = (SyncFlags)packet.getShortI();
            int         nSyncCountChanged = packet.getIntI();
            int         nSyncCountRemoved = packet.getIntI();
            string      szChangesUpto = packet.getString(14);
            short       bSyncJobStatus = packet.getByteS();
            
            string      szTemp = packet.getString(packet.RemainingBytesToRead);

            Debug.WriteLine("{0}.Jobs.SyncFromMessagePacket SyncFlags:{1} UpTo:{2} CountChanged:{3} CountRemoved:{4} ", tamiClient.Id, nSyncFlags, szChangesUpto, nSyncCountChanged, nSyncCountRemoved);

            //System.Windows.Forms.MessageBox.Show("action=" + action + "\nszTemp\n" + szTemp);

            Dictionary<string, Job> jobsNew;
            List<Job> listJobsChanged = new List<Job>(nSyncCountChanged);
            List<string> listJobsRemoved = new List<string>(nSyncCountRemoved);
            string jobId;
            Job job = null;
            string[] szItem;
            string[] szItems = szTemp.Split(TaMiClient.SEP_CHAR_ROW);

            /*
            if (nSyncCount != szItems.Length - 1)
            {
                System.Windows.Forms.MessageBox.Show("action=" + action + "\nSyncCount=" + nSyncCount + "\nszItems.Length=" + szItems.Length + "\nszTemp\n" + szTemp);
            }
            */

            if (nSyncFlags == SyncFlags.FULLSYNC)
                jobsNew = new Dictionary<string, Job>();
            else
                jobsNew = mJobs;


            lock (jobsNew)
            {
                for (int i = 0; i < nSyncCountChanged; i++)
                {
                    //Debug.WriteLine("JOBS: " + szItems[i]);
                    szItem = szItems[i].Split(TaMiClient.SEP_CHAR_COL);

                    //INFO: STATICDATALIST und DYNDATALIST haben eine leere Zeile am Ende anhängen, DYNDATASYNC dagegen nicht. 
                    //      Im Falle einer leeren Zeile wird jetzt abgebrochen.
                    if (szItem.Length < 2) { break; }

                    jobId = szItem[0];
                    if (jobId.Length > 0)
                    {
                        if (nSyncFlags == SyncFlags.FULLSYNC)
                        {
                            job = new Job();    
                        }
                        else
                        {
                            jobsNew.TryGetValue(jobId, out job);
                            if (job == null) { job = new Job(); }
                        }

                        job.DeserializeStatic(szItem);
                        jobsNew.Add(jobId, job);

                        //if (job == null) { job = new Job(); jobsNew.Add(jobId, job); }
                        //else if (nSyncFlags == SyncFlags.FULLSYNC) { jobsNew.Add(jobId, job); }

                        //job.DeserializeStatic(szItem);

                        listJobsChanged.Add(job);

                        //Debug.WriteLine("JOBS: " + job);
                    }

                } //for

                //Entferne die JobIds aus der RemovedList
                for (int i = 0; i < nSyncCountRemoved; i++)
                {
                    jobsNew.Remove(szItems[i]);
                    listJobsRemoved.Add(szItems[i]);
                }

                lock (mJobs)
                {
                    mJobs = jobsNew;
                }

                if (jobsNew.Count != (nSyncCountChanged - nSyncCountRemoved))
                    throw new Exception($"Jobs.Sync invalid counts. Count: {jobsNew.Count} SyncCountChanged: {nSyncCountChanged} SyncCountRemoved: {nSyncCountRemoved}");

            } // lock (jobsNew)

            JobsRemoved = listJobsRemoved;
            return listJobsChanged;
        }


    }
}
