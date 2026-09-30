import React, { useState, useEffect, useMemo, useCallback } from 'react';
import {
  SafeAreaView,
  Text,
  View,
  ScrollView,
  ActivityIndicator,
  Animated,
  TouchableOpacity,
  StatusBar,
} from 'react-native';
import styles from './ManagerSystemReports.styles';
import RoleHeader from '../components/ui/RoleHeader';
import LoginAquaticBackground from '../components/ui/LoginAquaticBackground';
import { API_BASE_URL, buildAuthHeaders } from '../apiConfig';
import { MaterialCommunityIcons } from '@expo/vector-icons';

export default function ManagerSystemReports({ navigation, route }) {
  const expectedMetricLabels = [
    'שליטה בנשימות (הכנסת ראש למים)',
    'יציבה וציפה',
    'הסתגלות וביטחון במים',
    'תנועתיות וקואורדינציה',
    'תקשורת במים (ושיתוף פעולה)',
    'התמדה ומאמץ',
  ];

  const managerAuth = route?.params?.managerAuth;
  const authToken = useMemo(() => String(managerAuth?.token || '').trim(), [managerAuth?.token]);
  const hasManagerAuth = authToken.length > 0;

  const [isLoading, setIsLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState('');
  const [generalSummary, setGeneralSummary] = useState('');
  const [groups, setGroups] = useState([]);
  
  const fadeAnim = useState(new Animated.Value(0))[0];

  const loadAiStats = useCallback(async () => {
    if (!hasManagerAuth) return;
    try {
      setIsLoading(true);
      setErrorMsg('');
      const response = await fetch(`${API_BASE_URL}/manager-groups/system-reports/group-stats`, {
        headers: buildAuthHeaders(authToken),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) throw new Error(payload?.message || 'שגיאה בטעינת נתונים');

      setGeneralSummary(payload?.aiSummary || '');
      setGroups(payload?.groups || []);

      Animated.timing(fadeAnim, {
        toValue: 1,
        duration: 800,
        useNativeDriver: true
      }).start();
      
    } catch (e) {
      setErrorMsg(e.message || 'לא ניתן לטעון סטטיסטיקות כרגע.');
    } finally {
      setIsLoading(false);
    }
  }, [authToken, hasManagerAuth, fadeAnim]);

  useEffect(() => { loadAiStats(); }, [loadAiStats]);

  const goToManagerHomepage = () => {
    navigation.navigate('ManagerHomepage', route?.params || {});
  };

  const getMetricIcon = (metric) => {
    const m = metric.toLowerCase();
    if (m.includes('שחי') || m.includes('תנועת') || m.includes('swim')) return 'swim';
    if (m.includes('נשימ') || m.includes('breath')) return 'weather-windy';
    if (m.includes('ציפ') || m.includes('יציבה') || m.includes('float')) return 'waves';
    if (m.includes('הסתגלות') || m.includes('ביטחון') || m.includes('anx')) return 'shield-check';
    if (m.includes('תקשורת') || m.includes('שיתוף')) return 'account-voice';
    if (m.includes('התמדה') || m.includes('מאמץ')) return 'arm-flex';
    if (m.includes('פחד') || m.includes('fear')) return 'emoticon-happy-outline';
    if (m.includes('התנהג') || m.includes('focus')) return 'brain';
    return 'star-four-points';
  };

  const buildDisplayMetrics = (medianMetrics) => {
    const normalized = medianMetrics || {};
    const metricsByLabel = new Map(Object.entries(normalized));
    const displayList = expectedMetricLabels.map((label) => ({
      label,
      value: metricsByLabel.has(label) ? Number(metricsByLabel.get(label)) : null,
    }));

    metricsByLabel.forEach((value, label) => {
      if (!expectedMetricLabels.includes(label)) {
        displayList.push({ label, value: Number(value) });
      }
    });

    return displayList;
  };

  return (
    <View style={styles.safeArea}>
      <StatusBar translucent backgroundColor="transparent" barStyle="light-content" />
      <LoginAquaticBackground />
      <RoleHeader
        title={'סטטיסטיקות ומדדים'}
        leftIcon="›"
        onLeftPress={goToManagerHomepage}
        showRightButton={false}
        theme="dark"
      />
      <ScrollView contentContainerStyle={styles.scrollContent} showsVerticalScrollIndicator={false}>
        {!hasManagerAuth && <Text style={styles.errorText}>נדרשת התחברות מנהל לטעינת נתונים.</Text>}

        {errorMsg !== '' && (
          <View style={[styles.stateCard, styles.stateCardError]}>
            <MaterialCommunityIcons name="alert-circle" size={22} color="#ff9a9a" />
            <Text style={styles.stateCardText}>{errorMsg}</Text>
          </View>
        )}

        {isLoading ? (
          <View style={styles.loadingWrap}>
            <ActivityIndicator size="large" color="#00d4ff" />
            <Text style={styles.loadingText}>
              מנתח נתונים באמצעות בינה מלאכותית...
            </Text>
          </View>
        ) : (
          <Animated.View style={[styles.animatedContent, { opacity: fadeAnim }]}>
            {generalSummary !== '' && (
              <View style={styles.summaryCard}>
                <View style={styles.summaryHeaderRow}>
                  <MaterialCommunityIcons name="robot-outline" size={24} color="#ffd38a" />
                  <Text style={styles.summaryTitle}>סיכום מתחם חכם</Text>
                </View>
                <Text style={styles.summaryText}>{generalSummary}</Text>
              </View>
            )}

            {groups.length === 0 && generalSummary === '' ? (
              <Text style={styles.emptyStateText}>לא נמצאו עדיין נתוני קבוצות להצגה.</Text>
            ) : null}

            {groups.map((g) => {
              const displayMetrics = buildDisplayMetrics(g.medianMetrics);

              return (
                <View key={g.groupId} style={styles.groupCard}>
                  <View style={styles.groupHeader}>
                    <Text style={styles.groupTitle}>{g.groupName}</Text>
                    <View style={styles.groupAccent} />
                  </View>

                  <View style={styles.metricGrid}>
                    {displayMetrics.map((metric) => (
                      <View key={metric.label} style={styles.metricCard}>
                        <MaterialCommunityIcons name={getMetricIcon(metric.label)} size={20} color="#58dcff" />
                        <View style={styles.metricInfo}>
                          <Text style={styles.metricLabel} numberOfLines={1}>{metric.label}</Text>
                          <Text style={styles.metricValue}>
                            {Number.isFinite(metric.value) ? `${metric.value}/5` : 'אין נתון'}
                          </Text>
                        </View>
                      </View>
                    ))}
                    {displayMetrics.length === 0 && (
                      <Text style={styles.metricsEmptyText}>טרם הוזנו מדדים עבור קבוצה זו.</Text>
                    )}
                  </View>

                  {g.aiNotes !== '' && (
                    <View style={styles.aiNotesWrap}>
                      <View style={styles.aiNotesHeader}>
                        <MaterialCommunityIcons name="robot-outline" size={18} color="#ffd46e" />
                        <Text style={styles.aiNotesTitle}>הצלחות ונתונים לשיפור: השבוע</Text>
                      </View>
                      <Text style={styles.aiNotesText}>{g.aiNotes}</Text>
                    </View>
                  )}
                </View>
              );
            })}
            
            <TouchableOpacity style={styles.refreshButton} onPress={loadAiStats}>
              <MaterialCommunityIcons name="refresh" size={20} color="#dff8ff" />
              <Text style={styles.refreshButtonText}>רענן מחדש תובנות חכמות</Text>
            </TouchableOpacity>

          </Animated.View>
        )}
      </ScrollView>
    </View>
  );
}
