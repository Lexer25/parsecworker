using ParsecIntegrationClient.IntegrationWebService;
using System;
using System.Collections.Generic;

namespace ParsecIntegrationClient.Models
{
    public class ClientState
    {
        public const int Result_Success = 0;
        public const int Result_ErrorDefault = -1;

        public const int Event_In = 0;
        public const int Event_Out = 1;

        private static string _domain = null;
        private static string _user = null;
        private static Guid _sessionID = Guid.Empty;
        private static Guid _rootOrgUnitID = Guid.Empty;
        private static Guid _rootTerritoryID = Guid.Empty;
        private static IDictionary<Guid, Guid> _orgUnitHierarhyDict = null;
        private static IDictionary<Guid, string> _orgUnitDict = null;
        private static bool _accGroupSet = false;
        private static IDictionary<Guid, string> _accGroupDict = null;

        public static void SetSession(Session newSession, string domain, string user)
        {
            if (newSession == null)
                return;
            _sessionID = newSession.SessionID;
            _rootOrgUnitID = newSession.RootOrgUnitID;
            _rootTerritoryID = newSession.RootTerritoryID;
            _domain = domain;
            _user = user;
        }

        public static void SetOrgUnitHierarhy(OrgUnit[] orgUnitHierarhyList)
        {
            if (_orgUnitHierarhyDict == null)
            {
                _orgUnitHierarhyDict = new Dictionary<Guid, Guid>();
                _orgUnitDict = new Dictionary<Guid, string>();
            }
            else
            {
                _orgUnitHierarhyDict.Clear();
                _orgUnitDict.Clear();
            }
            if (orgUnitHierarhyList == null || orgUnitHierarhyList.Length == 0)
                return;
            for (int i = orgUnitHierarhyList.Length - 1; i >= 0; i--)
            {
                if (orgUnitHierarhyList[i] == null || _orgUnitHierarhyDict.ContainsKey(orgUnitHierarhyList[i].ID))
                    continue;
                _orgUnitHierarhyDict.Add(orgUnitHierarhyList[i].ID, orgUnitHierarhyList[i].PARENT_ID);
                _orgUnitDict.Add(orgUnitHierarhyList[i].ID, orgUnitHierarhyList[i].NAME ?? string.Empty);
            }
        }

        public static void SetAccessGroupList(AccessGroup[] accGroupsList)
        {
            if (accGroupsList == null || accGroupsList.Length == 0)
                return;
            if (_accGroupDict == null)
                _accGroupDict = new Dictionary<Guid, string>();
            else
                _accGroupDict.Clear();
            _accGroupSet = false;
            for (int i = accGroupsList.Length - 1; i >= 0; i--)
            {
                if (accGroupsList[i] == null || _accGroupDict.ContainsKey(accGroupsList[i].ID))
                    continue;
                _accGroupDict.Add(accGroupsList[i].ID, accGroupsList[i].NAME ?? string.Empty);
            }
        }

        /// <summary>Сбрасывает сессию: Worker увидит пустой идентификатор и авторизуется заново.</summary>
        public static void ResetSession()
        {
            _sessionID = Guid.Empty;
        }

        public static Guid SessionID
        {
            get
            {
                return _sessionID;
            }
        }

        public static Guid RootOrgUnitID
        {
            get
            {
                return _rootOrgUnitID;
            }
        }

        public static Guid RootTerritoryID
        {
            get
            {
                return _rootTerritoryID;
            }
        }

        public static string CurrentUser
        {
            get
            {
                return string.Format("{0}\\{1}", _domain ?? string.Empty, _user ?? string.Empty);
            }
        }

        public static Guid GetParentOrgUnit(Guid orgUnitID)
        {
            if (_orgUnitHierarhyDict == null || !_orgUnitHierarhyDict.ContainsKey(orgUnitID))
                return Guid.Empty;
            return _orgUnitHierarhyDict[orgUnitID];
        }

        public static string GetOrgUnitName(Guid orgUnitID)
        {
            if (_orgUnitDict == null || !_orgUnitDict.ContainsKey(orgUnitID))
                return string.Empty;
            return _orgUnitDict[orgUnitID];
        }

        public static string GetAccGroupName(Guid accGroupID)
        {
            if (_orgUnitDict == null || !_accGroupDict.ContainsKey(accGroupID))
                return string.Empty;
            return _accGroupDict[accGroupID];
        }

        public static bool AccessGroupsListSet
        {
            get { return _accGroupSet; }
        }
    }
}

