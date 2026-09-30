import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  SafeAreaView,
  ScrollView,
  StatusBar,
  Text,
  TouchableOpacity,
  View,
} from 'react-native';
import styles from './ManagerAttendanceReport.styles';
import RoleHeader from '../components/ui/RoleHeader';
import LoginAquaticBackground from '../components/ui/LoginAquaticBackground';
import { API_BASE_URL, buildAuthHeaders } from '../apiConfig';

const formatDateForApi = (date) => {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${year}-${month}-${day}`;
};

export default function ManagerAttendanceReport({ navigation, route }) {
  const managerAuth = route?.params?.managerAuth;
  const authToken = useMemo(() => String(managerAuth?.token || '').trim(), [managerAuth?.token]);
  const hasManagerAuth = authToken.length > 0;

  const [groupCards, setGroupCards] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState('');
  const [expandedGroupId, setExpandedGroupId] = useState(null);
  const [lastSyncDate, setLastSyncDate] = useState('');

  const goToManagerHomepage = () => {
    navigation.navigate('ManagerHomepage', route?.params || {});
  };

  const loadAttendanceData = useCallback(async () => {
    if (!hasManagerAuth) {
      setGroupCards([]);
      setErrorMessage('לא זוהתה התחברות מנהל פעילה. התחברו מחדש כמנהל.');
      return;
    }

    try {
      setIsLoading(true);
      setErrorMessage('');

      const syncDate = formatDateForApi(new Date());

      const response = await fetch(`${API_BASE_URL}/manager-groups/attendance-groups-summary?includeInactive=true`, {
        headers: buildAuthHeaders(authToken),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון נתוני נוכחות קבוצות כרגע.');
      }

      const normalizedGroups = (Array.isArray(payload?.groups) ? payload.groups : [])
        .map((group) => {
          const rawChildren = Array.isArray(group?.children) ? group.children : [];

          return {
            groupId: Number(group?.groupId || 0),
            name: String(group?.groupName || group?.name || '').trim(),
            instructorFullName: String(group?.instructorFullName || '').trim(),
            isActive: Boolean(group?.isActive),
            totalAttendedCount: Number(group?.totalAttendedCount || 0),
            totalMeetingsCount: Number(group?.totalMeetingsCount || group?.totalCount || 0),
            averageAttendancePercent: group?.averageAttendancePercent == null
              ? null
              : Number(group.averageAttendancePercent),
            children: rawChildren
              .map((child) => ({
                childId: Number(child?.childId || 0),
                fullName: String(child?.childFullName || child?.fullName || '').trim(),
                parentFullName: String(child?.parentFullName || '').trim(),
                attendedCount: Number(child?.attendedCount || 0),
                totalCount: Number(child?.totalCount || 0),
                missedCount: Number(child?.missedCount || 0),
                attendancePercent: child?.attendancePercent == null
                  ? null
                  : Number(child.attendancePercent),
              }))
              .filter((child) => child.childId > 0)
              .sort((a, b) => a.fullName.localeCompare(b.fullName, 'he')),
          };
        })
        .filter((group) => group.groupId > 0)
        .sort((a, b) => a.name.localeCompare(b.name, 'he'));

      setGroupCards(normalizedGroups);

      const generatedAtRaw = String(payload?.generatedAtUtc || '').trim();
      setLastSyncDate(generatedAtRaw ? generatedAtRaw.slice(0, 10) : syncDate);
    } catch (error) {
      setGroupCards([]);
      setErrorMessage(error?.message || 'שגיאת טעינה לא צפויה.');
    } finally {
      setIsLoading(false);
    }
  }, [authToken, hasManagerAuth]);

  useEffect(() => {
    loadAttendanceData();
  }, [loadAttendanceData]);

  return (
    <View style={styles.safeArea}>
      <StatusBar translucent backgroundColor="transparent" barStyle="light-content" />
      <LoginAquaticBackground />
      <RoleHeader
        title={'נוכחות קבוצות'}
        leftIcon="›"
        onLeftPress={goToManagerHomepage}
        showRightButton={false}
        theme="dark"
      />

      <ScrollView contentContainerStyle={styles.scrollContent} showsVerticalScrollIndicator={false}>
        <View style={styles.mainCard}>
          <Text style={styles.sectionTitle}>כל הקבוצות במערכת</Text>

          {!hasManagerAuth ? (
            <Text style={styles.listStateText}>הפעולות מושבתות עד התחברות מנהל מחדש.</Text>
          ) : null}

          <Text style={styles.rangeText}>הנתונים מחושבים מכל ההיסטוריה (עד {lastSyncDate || formatDateForApi(new Date())})</Text>

          <View style={styles.controlsRow}>
            <TouchableOpacity
              style={styles.secondaryControlButton}
              activeOpacity={0.85}
              onPress={loadAttendanceData}
            >
              <Text style={styles.secondaryControlButtonText}>רענון נתונים</Text>
            </TouchableOpacity>
          </View>

          {isLoading ? (
            <Text style={styles.listStateText}>טוען קבוצות ונתוני נוכחות...</Text>
          ) : null}

          {!isLoading && errorMessage ? (
            <Text style={styles.errorText}>{errorMessage}</Text>
          ) : null}

          {!isLoading && !errorMessage && groupCards.length === 0 ? (
            <Text style={styles.listStateText}>לא נמצאו קבוצות להצגה.</Text>
          ) : null}

          {!isLoading && !errorMessage
            ? groupCards.map((group) => {
              const isExpanded = expandedGroupId === group.groupId;
              const groupPercent = group.averageAttendancePercent;
              const groupRatioText = `${group.totalAttendedCount}/${group.totalMeetingsCount}`;

              return (
                <View key={String(group.groupId)} style={styles.groupCard}>
                  <TouchableOpacity
                    activeOpacity={0.9}
                    style={styles.groupHeaderButton}
                    onPress={() => setExpandedGroupId((prev) => (prev === group.groupId ? null : group.groupId))}
                  >
                    <View style={styles.groupHeaderMain}>
                      <Text style={styles.groupName}>{group.name || `קבוצה ${group.groupId}`}</Text>
                      <Text style={styles.groupMetaText}>מדריך: {group.instructorFullName || 'לא משויך'}</Text>
                      <Text style={styles.groupMetaText}>ילדים בקבוצה: {group.children.length}</Text>
                      {!group.isActive ? <Text style={styles.groupInactiveText}>קבוצה לא פעילה</Text> : null}
                    </View>

                    <View style={styles.groupAttendanceWrap}>
                      <Text style={styles.groupAttendanceLabel}>נוכחות ממוצעת</Text>
                      <Text style={styles.groupAttendanceValue}>{groupPercent == null ? 'אין נתונים' : `${groupPercent}%`}</Text>
                      <Text style={styles.groupAttendanceRatio}>{groupRatioText}</Text>
                      <Text style={styles.groupExpandHint}>{isExpanded ? 'הסתר ילדים ▲' : 'הצג ילדים ▼'}</Text>
                    </View>
                  </TouchableOpacity>

                  {isExpanded ? (
                    <View style={styles.childrenPanel}>
                      {group.children.length === 0 ? (
                        <Text style={styles.listStateText}>אין כרגע ילדים משויכים לקבוצה זו.</Text>
                      ) : null}

                      {group.children.map((child) => (
                        <View key={String(child.childId)} style={styles.childRow}>
                          <View style={styles.childAttendanceWrap}>
                            <Text style={styles.childAttendanceValue}>
                              {child.attendancePercent == null ? 'אין נתונים' : `${child.attendancePercent}%`}
                            </Text>
                            <Text style={styles.childAttendanceRatio}>{`${child.attendedCount}/${child.totalCount}`}</Text>
                            <Text style={styles.childMissedText}>{`חיסורים: ${child.missedCount}`}</Text>
                          </View>

                          <View style={styles.childMetaWrap}>
                            <Text style={styles.childName}>{child.fullName}</Text>
                            <Text style={styles.childMetaText}>{`הורה: ${child.parentFullName || 'לא משויך'}`}</Text>
                          </View>
                        </View>
                      ))}
                    </View>
                  ) : null}
                </View>
              );
            })
            : null}
        </View>
      </ScrollView>
    </View>
  );
}
