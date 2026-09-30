import React, { useRef, useEffect, useState, useMemo, useCallback } from 'react';
import {
  Alert, StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  TextInput, StatusBar, Easing, Platform, ScrollView, Switch, ActivityIndicator,
  SafeAreaView, I18nManager, KeyboardAvoidingView,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import { useNavigation } from '@react-navigation/native';
import PrimaryButton from '../components/ui/PrimaryButton';
import AppCard from '../components/ui/AppCard';
import RoleHeader from '../components/ui/RoleHeader';
import { colors, shadows } from '../theme/tokens';
import { API_BASE_URL } from '../apiConfig';


const { width, height } = Dimensions.get('window');
const AnimatedBlurView = Animated.createAnimatedComponent(BlurView);

const exerciseCatalog = [
  { key: 'front_float', title: 'ציפה על הבטן' },
  { key: 'back_float', title: 'ציפה על הגב' },
  { key: 'front_kicks', title: 'בעיטות בטן' },
  { key: 'back_kicks', title: 'בעיטות גב' },
  { key: 'arrow_jump', title: 'קפיצה חץ' },
  { key: 'deep_jump', title: 'קפיצה עמוק' },
  { key: 'hoop_pass', title: 'חישוק' },
  { key: 'water_confidence', title: 'ביטחון במים' },
  { key: 'breathing_control', title: 'שליטה בנשימות (הכנסת ראש למים)' },
];

const targetMetrics = [
  "הסתגלות וביטחון במים",
  "שליטה בנשימות (הכנסת ראש למים)",
  "תנועתיות וקואורדינציה",
  "יציבה וציפה",
  "תקשורת במים (ושיתוף פעולה)",
  "התמדה ומאמץ",
  "יוזמה",
  "קשב וריכוז",
  "תגובה להוראות",
  "עצמאות בתרגיל"
];

// ========================================
// 1. SEAMLESS ANIMATED WAVES
// ========================================
const Wave = ({ color, duration, startPosition, d }) => {
  const translateX = useRef(new Animated.Value(startPosition)).current;

  useEffect(() => {
    const toPosition = startPosition === 0 ? -600 : 0;
    Animated.loop(
      Animated.sequence([
        Animated.timing(translateX, {
          toValue: toPosition,
          duration: duration / 2,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
        Animated.timing(translateX, {
          toValue: startPosition,
          duration: duration / 2,
          easing: Easing.inOut(Easing.sin),
          useNativeDriver: true,
        }),
      ]),
    ).start();
  }, [duration, startPosition]);

  return (
    <Animated.View style={[styles.waveContainer, { transform: [{ translateX }] }]}>
      <Svg width={2400} height={150} viewBox="0 0 2400 150" preserveAspectRatio="none">
        <Path fill={color} d={d} />
      </Svg>
    </Animated.View>
  );
};


// ========================================
// 3. MAIN COMPONENT
// ========================================
export default function InstructorAIGroupReports({ route }) {
  const navigation = useNavigation();
  const authUser = route?.params?.authUser;
  const instructorId = Number(authUser?.id || 0);


  const scrollY = useRef(new Animated.Value(0)).current;

  const surfaceOpacity = scrollY.interpolate({
    inputRange: [0, height * 0.8],
    outputRange: [1, 0],
    extrapolate: 'clamp',
  });

  const [step, setStep] = useState('setup'); // 'setup' | 'review'

  // Setup fields
  const [groups, setGroups] = useState([]);
  const [selectedGroupId, setSelectedGroupId] = useState(null);
  const [availableDates, setAvailableDates] = useState([]);
  const [selectedDate, setSelectedDate] = useState('');
  const [selectedExerciseKey, setSelectedExerciseKey] = useState(exerciseCatalog[0].key);
  const [generalComment, setGeneralComment] = useState('');
  const [invitations, setInvitations] = useState([]);
  const [selectedMetrics, setSelectedMetrics] = useState(targetMetrics);

  const [isLoadingSetup, setIsLoadingSetup] = useState(false);
  const [isLoadingDrafts, setIsLoadingDrafts] = useState(false);
  const [isSavingReports, setIsSavingReports] = useState(false);

  // Drafts
  const [drafts, setDrafts] = useState([]);

  const selectedExerciseTitle = useMemo(() => {
    return exerciseCatalog.find(e => e.key === selectedExerciseKey)?.title || '';
  }, [selectedExerciseKey]);

  // Load instructor groups
  useEffect(() => {
    if (!instructorId) return;

    const loadGroups = async () => {
      try {
        setIsLoadingSetup(true);
        const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/groups`);
        const payload = await response.json().catch(() => null);
        if (response.ok && Array.isArray(payload)) {
          setGroups(payload);
          if (payload.length > 0) {
            setSelectedGroupId(payload[0].groupId);
          }
        }
      } catch (error) {
        Alert.alert('שגיאה', 'לא ניתן לטעון את קבוצות המדריך.');
      } finally {
        setIsLoadingSetup(false);
      }
    };

    loadGroups();
  }, [instructorId]);

  // Load lesson dates when group changes
  useEffect(() => {
    if (!instructorId || !selectedGroupId) return;

    const loadDates = async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/invitations`);
        const payload = await response.json().catch(() => null);
        if (response.ok && Array.isArray(payload)) {
          const today = new Date();
          const todayStr = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;

          const filteredInvitations = payload.filter((inv) => {
            if (inv.status === 'Cancelled' || inv.status === 'CancelledByInstructor') return false;
            if (Number(inv.groupId) !== selectedGroupId) return false;
            if (inv.hasReport) return false; // exclude reported lessons

            return inv.meetingDate < todayStr; // strictly in the past (before today)
          });

          setInvitations(filteredInvitations);

          const groupDates = filteredInvitations
            .map((inv) => inv.meetingDate)
            .filter((d, i, arr) => arr.indexOf(d) === i)
            .sort((a, b) => b.localeCompare(a));

          setAvailableDates(groupDates);
          if (groupDates.length > 0) {
            setSelectedDate(groupDates[0]);
          } else {
            // Default to today formatted YYYY-MM-DD
            const y = today.getFullYear();
            const m = String(today.getMonth() + 1).padStart(2, '0');
            const d = String(today.getDate()).padStart(2, '0');
            setSelectedDate(`${y}-${m}-${d}`);
          }
        }
      } catch (error) {
        // Fallback to today
        const today = new Date();
        const y = today.getFullYear();
        const m = String(today.getMonth() + 1).padStart(2, '0');
        const d = String(today.getDate()).padStart(2, '0');
        setSelectedDate(`${y}-${m}-${d}`);
      }
    };

    loadDates();
  }, [instructorId, selectedGroupId]);

  // Automatically select the exercise based on the selected lesson date
  useEffect(() => {
    if (!selectedDate || invitations.length === 0) return;

    // Find the invitation for this date
    const inv = invitations.find(i => i.meetingDate === selectedDate);
    if (inv && inv.targetMetric) {
      const metric = inv.targetMetric.trim();
      let matchedKey = null;

      if (metric === "הסתגלות וביטחון במים" || metric === "ביטחון במים") {
        matchedKey = "water_confidence";
      } else if (metric.includes("נשימות") || metric.includes("ראש למים")) {
        matchedKey = "breathing_control";
      } else if (metric === "בעיטות בטן" || metric.includes("בעיטות בטן")) {
        matchedKey = "front_kicks";
      } else if (metric === "בעיטות גב" || metric.includes("בעיטות גב")) {
        matchedKey = "back_kicks";
      } else if (metric === "ציפה על הבטן" || metric.includes("ציפה על הבטן") || metric.includes("יציבה וציפה")) {
        matchedKey = "front_float";
      } else if (metric === "ציפה על הגב" || metric.includes("ציפה על הגב")) {
        matchedKey = "back_float";
      } else if (metric === "קפיצה חץ" || metric.includes("חץ")) {
        matchedKey = "arrow_jump";
      } else if (metric === "קפיצה עמוק" || metric.includes("עמוק")) {
        matchedKey = "deep_jump";
      } else if (metric.includes("חישוק")) {
        matchedKey = "hoop_pass";
      } else {
        const exercise = exerciseCatalog.find(
          ex => ex.title.trim() === metric || ex.key === metric
        );
        if (exercise) {
          matchedKey = exercise.key;
        }
      }

      if (matchedKey) {
        setSelectedExerciseKey(matchedKey);
      }
    }
  }, [selectedDate, invitations]);

  // Call AI bulk draft generation
  const handleGenerateDrafts = async () => {
    if (!selectedGroupId || !selectedDate || !generalComment.trim()) {
      Alert.alert('שגיאה', 'נא למלא את כל השדות כולל תיאור השיעור.');
      return;
    }

    try {
      setIsLoadingDrafts(true);
      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/groups/${selectedGroupId}/ai-bulk-reports-draft`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          generalComment: generalComment.trim(),
          exerciseKey: selectedExerciseKey,
          exerciseTitle: selectedExerciseTitle,
          reportDate: selectedDate,
          selectedMetrics: selectedMetrics,
        }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'שגיאה ביצירת הטיוטות.');
      }

      const draftsList = (Array.isArray(payload) ? payload : []).map(draft => {
        const mappedMetrics = (draft.metrics || []).map(m => ({
          ...m,
          isIncluded: selectedMetrics.includes(m.label),
        }));
        return {
          ...draft,
          metrics: mappedMetrics,
          included: true,
          instructorSummary: '',
        };
      });

      if (draftsList.length === 0) {
        Alert.alert('שימו לב', 'לא נמצאו ילדים פעילים בקבוצה זו.');
        return;
      }

      setDrafts(draftsList);
      setStep('review');
    } catch (error) {
      Alert.alert('שגיאה', error.message || 'תהליך יצירת הטיוטות נכשל. ודא כי שירות ה-AI מוגדר כראוי בשרת.');
    } finally {
      setIsLoadingDrafts(false);
    }
  };

  // Adjust metric value for a specific child
  const handleUpdateMetric = (childId, label, nextValue) => {
    setDrafts(prev => prev.map(draft => {
      if (draft.childId !== childId) return draft;
      const nextMetrics = draft.metrics.map(m => {
        if (m.label !== label) return m;
        return { ...m, value: nextValue };
      });
      return { ...draft, metrics: nextMetrics };
    }));
  };

  // Adjust comment for a specific child
  const handleUpdateComment = (childId, nextComment) => {
    setDrafts(prev => prev.map(draft => {
      if (draft.childId !== childId) return draft;
      return { ...draft, comment: nextComment };
    }));
  };

  // Adjust instructor summary for a specific child
  const handleUpdateInstructorSummary = (childId, nextSummary) => {
    setDrafts(prev => prev.map(draft => {
      if (draft.childId !== childId) return draft;
      return { ...draft, instructorSummary: nextSummary };
    }));
  };

  // Toggle isIncluded flag for a specific child's metric
  const handleToggleMetricIncluded = (childId, label, isIncluded) => {
    setDrafts(prev => prev.map(draft => {
      if (draft.childId !== childId) return draft;
      const nextMetrics = draft.metrics.map(m => {
        if (m.label !== label) return m;
        return { ...m, isIncluded };
      });
      return { ...draft, metrics: nextMetrics };
    }));
  };

  // Toggle included status
  const handleToggleIncluded = (childId) => {
    setDrafts(prev => prev.map(draft => {
      if (draft.childId !== childId) return draft;
      return { ...draft, included: !draft.included };
    }));
  };

  // Toggle attendance status
  const handleToggleAttendance = (childId) => {
    setDrafts(prev => prev.map(draft => {
      if (draft.childId !== childId) return draft;
      return { ...draft, isPresent: !draft.isPresent };
    }));
  };

  // Bulk save reports
  const handleSaveReports = async () => {
    const activeDrafts = drafts.filter(d => d.included);
    if (activeDrafts.length === 0) {
      Alert.alert('שגיאה', 'נא לכלול לפחות ילד אחד בשליחה.');
      return;
    }

    try {
      setIsSavingReports(true);
      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/reports-bulk`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          groupId: selectedGroupId,
          exerciseKey: selectedExerciseKey,
          exerciseTitle: selectedExerciseTitle,
          reportDate: selectedDate,
          drafts: activeDrafts.map(d => ({
            childId: d.childId,
            isPresent: d.isPresent,
            comment: d.instructorSummary.trim()
              ? `${d.comment}\n\nסיכום מדריך:\n${d.instructorSummary.trim()}`
              : d.comment,
            metrics: d.metrics,
          })),
        }),
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'שמירת הדיווחים נכשלה.');
      }

      Alert.alert('הצלחה', `נשמרו בהצלחה ${payload.savedCount} דיווחי התקדמות ונשלחו התראות להורים!`);
      navigation.goBack();
    } catch (error) {
      Alert.alert('שגיאה', error.message || 'שמירת הדיווחים נכשלה.');
    } finally {
      setIsSavingReports(false);
    }
  };

  return (
    <View style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="transparent" translucent />

      {/* Deep Ocean Base */}
      <LinearGradient colors={['#005a80', '#00456a', colors.bgDeep]} style={StyleSheet.absoluteFill} />

      {/* Surface Water */}
      <Animated.View style={[StyleSheet.absoluteFill, { opacity: surfaceOpacity }]}>
        <LinearGradient colors={['#00aed8', '#007fa7', '#004d73']} style={{ flex: 1 }} />
      </Animated.View>

      {/* Bottom Waves */}
      <Wave
        color="rgba(0, 153, 204, 0.3)"
        duration={8000}
        startPosition={0}
        d="M0,75 C200,45 400,105 600,75 C800,45 1000,105 1200,75 L1200,150 L0,150 Z M1200,75 C1400,45 1600,105 1800,75 C2000,45 2200,105 2400,75 L2400,150 L1200,150 Z"
      />
      <Wave
        color="rgba(0, 212, 255, 0.25)"
        duration={6000}
        startPosition={-600}
        d="M0,100 C300,60 500,130 800,90 C1000,60 1100,120 1200,100 L1200,150 L0,150 Z M1200,100 C1500,60 1700,130 2000,90 C2200,60 2300,120 2400,100 L2400,150 L1200,150 Z"
      />

      <SafeAreaView style={styles.headerSafeArea}>
        <RoleHeader
          title={step === 'setup' ? "דיווחים קבוצתיים" : "עריכה ואישור דוחות"}
          showLeftButton={true}
          leftIcon="›"
          onLeftPress={() => {
            if (step === 'review') {
              setStep('setup');
            } else {
              navigation.goBack();
            }
          }}
          showRightButton={false}
          theme="dark"
        />
      </SafeAreaView>

      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        style={{ flex: 1 }}
      >
        <Animated.ScrollView
          onScroll={Animated.event(
            [{ nativeEvent: { contentOffset: { y: scrollY } } }],
            { useNativeDriver: false },
          )}
          scrollEventThrottle={16}
          contentContainerStyle={styles.scrollContent}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          {isLoadingSetup ? (
            <ActivityIndicator size="large" color="#00d4ff" style={{ marginTop: 50 }} />
          ) : step === 'setup' ? (
            // SETUP VIEW
            <View style={styles.section}>
              <AppCard useBlur blurIntensity={30} blurTint="dark" style={styles.setupCard}>

                {/* Group selection */}
                <Text style={styles.label}>1. בחרו קבוצת אימון</Text>
                <View style={[styles.pickerContainer, { flexDirection: I18nManager.isRTL ? 'row-reverse' : 'row', justifyContent: I18nManager.isRTL ? 'flex-end' : 'flex-start' }]}>
                  {groups.map((g) => (
                    <TouchableOpacity
                      key={g.groupId}
                      onPress={() => setSelectedGroupId(g.groupId)}
                      style={[styles.groupChip, selectedGroupId === g.groupId && styles.groupChipActive]}
                    >
                      <Text style={[styles.groupChipText, selectedGroupId === g.groupId && styles.groupChipTextActive]}>
                        {g.name}
                      </Text>
                    </TouchableOpacity>
                  ))}
                </View>

                {/* Date selection */}
                <Text style={styles.label}>2. בחרו תאריך שיעור</Text>
                <View style={[styles.pickerContainer, { flexDirection: I18nManager.isRTL ? 'row-reverse' : 'row', justifyContent: I18nManager.isRTL ? 'flex-end' : 'flex-start' }]}>
                  {availableDates.length === 0 ? (
                    <Text style={styles.subtext}>לא נמצאו תאריכים מתוזמנים. ייווצר תאריך דיווח עבור היום.</Text>
                  ) : (
                    availableDates.slice(0, 5).map((d) => {
                      const label = new Date(d + 'T00:00:00').toLocaleDateString('he-IL', { day: '2-digit', month: '2-digit' });
                      return (
                        <TouchableOpacity
                          key={d}
                          onPress={() => setSelectedDate(d)}
                          style={[styles.groupChip, selectedDate === d && styles.groupChipActive]}
                        >
                          <Text style={[styles.groupChipText, selectedDate === d && styles.groupChipTextActive]}>
                            {label}
                          </Text>
                        </TouchableOpacity>
                      );
                    })
                  )}
                </View>

                {/* Exercise selection */}
                <Text style={styles.label}>3. תרגיל מרכזי שנבחר לשיעור</Text>
                <View style={[styles.pickerContainer, { flexDirection: I18nManager.isRTL ? 'row-reverse' : 'row', justifyContent: I18nManager.isRTL ? 'flex-end' : 'flex-start' }]}>
                  {exerciseCatalog.map((ex) => (
                    <TouchableOpacity
                      key={ex.key}
                      disabled={true}
                      style={[
                        styles.exerciseChip,
                        selectedExerciseKey === ex.key && styles.exerciseChipActive,
                        { opacity: selectedExerciseKey === ex.key ? 1 : 0.4 }
                      ]}
                    >
                      <Text style={[styles.exerciseChipText, selectedExerciseKey === ex.key && styles.exerciseChipTextActive]}>
                        {ex.title}
                      </Text>
                    </TouchableOpacity>
                  ))}
                </View>

                {/* Target metrics filter selection */}
                <Text style={styles.label}>4. בחרו מדדים שעבדתם עליהם היום (אופציונלי)</Text>
                <View style={[styles.pickerContainer, { flexDirection: I18nManager.isRTL ? 'row-reverse' : 'row', justifyContent: I18nManager.isRTL ? 'flex-end' : 'flex-start' }]}>
                  {targetMetrics.map((metric) => {
                    const isSel = selectedMetrics.includes(metric);
                    return (
                      <TouchableOpacity
                        key={metric}
                        onPress={() => {
                          if (isSel) {
                            setSelectedMetrics(prev => prev.filter(m => m !== metric));
                          } else {
                            setSelectedMetrics(prev => [...prev, metric]);
                          }
                        }}
                        style={[styles.groupChip, isSel && styles.groupChipActive]}
                      >
                        <Text style={[styles.groupChipText, isSel && styles.groupChipTextActive]}>
                          {metric}
                        </Text>
                      </TouchableOpacity>
                    );
                  })}
                </View>

                {/* General comment */}
                <Text style={styles.label}>5. תארו בקצרה את השיעור</Text>
                <TextInput
                  value={generalComment}
                  onChangeText={setGeneralComment}
                  placeholder={"ציינו את שמות הילדים ומדדים ספציפיים לתוצאה מדויקת.\nלמשל: רוני היה מעולה בקשב וריכוז ובתגובה להוראות, אך צריך שיפור בעצמאות בתרגיל. דני פחד מהמים בהתחלה אבל השתפר לקראת הסוף. שאר הקבוצה היו טובים בכל המדדים."}
                  placeholderTextColor="rgba(255,255,255,0.4)"
                  multiline
                  numberOfLines={5}
                  style={styles.textArea}
                  textAlign={I18nManager.isRTL ? 'left' : 'right'}
                />

                <PrimaryButton
                  label={isLoadingDrafts ? "מייצר טיוטות..." : "ייצר טיוטות בעזרת AI ✨"}
                  onPress={handleGenerateDrafts}
                  disabled={isLoadingDrafts || !generalComment.trim()}
                  style={styles.generateBtn}
                  colorsOverride={isLoadingDrafts || !generalComment.trim() ? ['rgba(0,212,255,0.3)', 'rgba(0,153,204,0.3)'] : null}
                />
              </AppCard>
            </View>
          ) : (
            // REVIEW & SEND VIEW
            <View style={styles.section}>
              <Text style={styles.sectionSubtitle}>
                ה-AI יצר טיוטות אישיות לכל ילד. עברו עליהן, ערכו מדדים או הערות במידת הצורך ולחצו שליחה.
              </Text>

              {drafts.map((draft) => (
                <AppCard key={draft.childId} useBlur blurIntensity={20} blurTint="dark" style={styles.draftCard}>
                  <View style={[styles.draftCardHeader, { flexDirection: I18nManager.isRTL ? 'row-reverse' : 'row' }]}>
                    <Text style={styles.childName}>{draft.childName}</Text>
                  </View>

                  <View style={styles.draftContent}>
                    {/* Attendance */}
                    <View style={[styles.attendanceRow, { flexDirection: I18nManager.isRTL ? 'row-reverse' : 'row' }]}>
                      <Text style={styles.attendanceLabel}>נוכחות בשיעור:</Text>
                      <View style={{ flexDirection: 'row', alignItems: 'center', gap: 10 }}>
                        <Text style={styles.attendanceStatusText}>{draft.isPresent ? 'נכח/ה' : 'נעדר/ה'}</Text>
                        <Switch
                          value={draft.isPresent}
                          onValueChange={() => handleToggleAttendance(draft.childId)}
                          trackColor={{ false: '#ef4444', true: '#10b981' }}
                        />
                      </View>
                    </View>

                    {draft.isPresent && (
                      <View>
                        {/* Metrics values list */}
                        <Text style={styles.subLabel}>ציוני מדדים (0 עד 5):</Text>
                        {draft.metrics.map((m) => {
                          if (m.isIncluded === false) return null;
                          return (
                            <View key={m.label} style={[styles.metricRow, { flexDirection: I18nManager.isRTL ? 'row-reverse' : 'row' }]}>
                              <Text style={styles.metricName}>{m.label}</Text>
                              <TouchableOpacity
                                onPress={() => handleToggleMetricIncluded(draft.childId, m.label, false)}
                                style={styles.metricRemoveBtn}
                              >
                                <Text style={styles.metricRemoveBtnText}>✕</Text>
                              </TouchableOpacity>
                              <View style={[styles.ratingContainer, { flexDirection: I18nManager.isRTL ? 'row' : 'row-reverse' }]}>
                                {[0, 1, 2, 3, 4, 5].map((val) => (
                                  <TouchableOpacity
                                    key={val}
                                    onPress={() => handleUpdateMetric(draft.childId, m.label, val)}
                                    style={[styles.ratingBtn, m.value === val && styles.ratingBtnActive]}
                                    hitSlop={{ top: 10, bottom: 10, left: 6, right: 6 }}
                                  >
                                    <Text style={[styles.ratingText, m.value === val && styles.ratingTextActive]}>
                                      {val}
                                    </Text>
                                  </TouchableOpacity>
                                ))}
                              </View>
                            </View>
                          );
                        })}

                        {/* Excluded metrics list */}
                        {draft.metrics.some(m => m.isIncluded === false) && (
                          <View style={styles.removedMetricsContainer}>
                            <Text style={styles.removedMetricsTitle}>מדדים שהוסרו (לחץ להחזרה):</Text>
                            <View style={[styles.removedMetricsList, { flexDirection: I18nManager.isRTL ? 'row-reverse' : 'row', justifyContent: I18nManager.isRTL ? 'flex-end' : 'flex-start' }]}>
                              {draft.metrics.map((m) => {
                                if (m.isIncluded !== false) return null;
                                return (
                                  <TouchableOpacity
                                    key={m.label}
                                    onPress={() => handleToggleMetricIncluded(draft.childId, m.label, true)}
                                    style={styles.restoreChip}
                                  >
                                    <Text style={styles.restoreChipText}>↩ {m.label}</Text>
                                  </TouchableOpacity>
                                );
                              })}
                            </View>
                          </View>
                        )}

                        {/* AI Comment Draft */}
                        <Text style={styles.subLabel}>הערה אוטומטית מה-AI:</Text>
                        <TextInput
                          value={draft.comment}
                          onChangeText={(txt) => handleUpdateComment(draft.childId, txt)}
                          multiline
                          style={styles.commentInput}
                          textAlign={I18nManager.isRTL ? 'left' : 'right'}
                        />

                        {/* Instructor Summary (Optional) */}
                        <Text style={styles.subLabel}>סיכום מדריך (אופציונלי):</Text>
                        <TextInput
                          value={draft.instructorSummary}
                          onChangeText={(txt) => handleUpdateInstructorSummary(draft.childId, txt)}
                          placeholder="הוסיפו הערה אישית משלכם כאן..."
                          placeholderTextColor="rgba(255,255,255,0.4)"
                          multiline
                          style={styles.commentInput}
                          textAlign={I18nManager.isRTL ? 'left' : 'right'}
                        />
                      </View>
                    )}
                  </View>
                </AppCard>
              ))}

              <View style={[styles.actionButtonsRow, { flexDirection: I18nManager.isRTL ? 'row-reverse' : 'row' }]}>
                <TouchableOpacity
                  onPress={() => setStep('setup')}
                  style={styles.cancelBtn}
                  disabled={isSavingReports}
                >
                  <Text style={styles.cancelBtnText}>חזרה לעריכה</Text>
                </TouchableOpacity>
                <PrimaryButton
                  label={isSavingReports ? "שולח דיווחים..." : "שלח דוחות מאושרים 🚀"}
                  onPress={handleSaveReports}
                  disabled={isSavingReports || drafts.filter(d => d.included).length === 0}
                  style={styles.submitBtn}
                  textStyle={styles.submitBtnText}
                  colorsOverride={isSavingReports || drafts.filter(d => d.included).length === 0 ? ['rgba(0,212,255,0.3)', 'rgba(0,153,204,0.3)'] : ['#00d4ff', '#00d4ff']}
                />
              </View>
            </View>
          )}
        </Animated.ScrollView>
      </KeyboardAvoidingView>
    </View>
  );
}

// ========================================
// STYLES
// ========================================
const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep },
  headerSafeArea: {
    width: '100%',
    paddingTop: Platform.OS === 'ios' ? 0 : (Platform.OS === 'android' ? StatusBar.currentHeight : 0),
    backgroundColor: 'transparent',
    zIndex: 12,
  },
  scrollContent: { paddingTop: 16, paddingBottom: 60, paddingHorizontal: 16 },
  waveContainer: { position: 'absolute', bottom: 0, left: (width - 2400) / 2, width: 2400, height: 150, zIndex: 0 },

  section: { zIndex: 10, marginTop: 10 },
  sectionSubtitle: {
    color: 'rgba(255, 255, 255, 0.72)',
    fontSize: 14,
    textAlign: 'left',
    alignSelf: 'stretch',
    writingDirection: 'rtl',
    lineHeight: 22,
    marginBottom: 20,
  },
  setupCard: {
    padding: 20,
    borderRadius: 24,
    backgroundColor: 'rgba(255, 255, 255, 0.08)',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.16)',
  },
  label: {
    color: '#00d4ff',
    fontSize: 16,
    fontWeight: '800',
    textAlign: 'left',
    alignSelf: 'stretch',
    writingDirection: 'rtl',
    marginTop: 15,
    marginBottom: 10,
  },
  subtext: {
    color: 'rgba(255, 255, 255, 0.5)',
    fontSize: 13,
    textAlign: I18nManager.isRTL ? 'left' : 'right',
    alignSelf: 'stretch',
    writingDirection: 'rtl',
    marginBottom: 10,
  },
  pickerContainer: {
    flexWrap: 'wrap',
    gap: 8,
    marginBottom: 12,
  },
  groupChip: {
    backgroundColor: 'rgba(255, 255, 255, 0.06)',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.12)',
    borderRadius: 16,
    paddingHorizontal: 16,
    paddingVertical: 8,
  },
  groupChipActive: {
    backgroundColor: '#00d4ff',
    borderColor: '#00d4ff',
  },
  groupChipText: {
    color: 'rgba(255, 255, 255, 0.8)',
    fontSize: 13,
    fontWeight: '600',
  },
  groupChipTextActive: {
    color: '#001529',
    fontWeight: '800',
  },
  exerciseScroll: {
    paddingVertical: 4,
    marginBottom: 12,
    gap: 8,
    flexDirection: 'row',
  },
  exerciseChip: {
    backgroundColor: 'rgba(255, 255, 255, 0.06)',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.12)',
    borderRadius: 16,
    paddingHorizontal: 16,
    paddingVertical: 8,
  },
  exerciseChipActive: {
    backgroundColor: '#00d4ff',
    borderColor: '#00d4ff',
  },
  exerciseChipText: {
    color: 'rgba(255, 255, 255, 0.8)',
    fontSize: 13,
    fontWeight: '600',
  },
  exerciseChipTextActive: {
    color: '#001529',
    fontWeight: '800',
  },
  textArea: {
    backgroundColor: 'rgba(255, 255, 255, 0.05)',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.15)',
    borderRadius: 16,
    padding: 14,
    color: '#fff',
    fontSize: 14,
    minHeight: 100,
    textAlignVertical: 'top',
    marginBottom: 20,
    textAlign: I18nManager.isRTL ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  generateBtn: {
    marginTop: 10,
    borderRadius: 16,
  },

  // DRAFTS REVIEW
  draftCard: {
    padding: 12,
    borderRadius: 16,
    backgroundColor: 'rgba(255, 255, 255, 0.06)',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.12)',
    marginBottom: 12,
  },
  draftCardHeader: {
    justifyContent: 'flex-start',
    alignItems: 'center',
    borderBottomWidth: 1,
    borderBottomColor: 'rgba(255, 255, 255, 0.1)',
    paddingBottom: 8,
    marginBottom: 8,
  },
  draftHeaderLeft: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
  },
  childName: {
    color: '#fff',
    fontSize: 16,
    fontWeight: '800',
    textAlign: I18nManager.isRTL ? 'left' : 'right',
    writingDirection: 'rtl',
    alignSelf: 'stretch',
  },
  includeLabel: {
    color: 'rgba(255,255,255,0.6)',
    fontSize: 13,
    fontWeight: '600',
  },
  draftContent: {
    marginTop: 6,
  },
  attendanceRow: {
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: 'rgba(255,255,255,0.04)',
    borderRadius: 12,
    padding: 10,
    marginBottom: 16,
  },
  attendanceLabel: {
    color: 'rgba(255,255,255,0.8)',
    fontSize: 14,
    fontWeight: '700',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  attendanceStatusText: {
    color: '#fff',
    fontSize: 14,
    fontWeight: '600',
  },
  subLabel: {
    color: '#00d4ff',
    fontSize: 14,
    fontWeight: '800',
    textAlign: 'left',
    alignSelf: 'stretch',
    writingDirection: 'rtl',
    marginTop: 10,
    marginBottom: 8,
  },
  metricRow: {
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 6,
    backgroundColor: 'rgba(255,255,255,0.03)',
    paddingVertical: 5,
    paddingHorizontal: 8,
    borderRadius: 8,
  },
  metricLabelContainer: {
    alignItems: 'center',
    flex: 1,
    justifyContent: 'flex-start',
    gap: 6,
  },
  metricName: {
    color: '#fff',
    fontSize: 12,
    flex: 1,
    textAlign: I18nManager.isRTL ? 'right' : 'left',
  },
  ratingContainer: {
    gap: 3,
  },
  ratingBtn: {
    width: 28,
    height: 28,
    borderRadius: 14,
    backgroundColor: 'rgba(255,255,255,0.08)',
    justifyContent: 'center',
    alignItems: 'center',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.2)',
  },
  ratingBtnActive: {
    backgroundColor: '#00d4ff',
    borderColor: '#00d4ff',
    shadowColor: '#00d4ff',
    shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0.5,
    shadowRadius: 5,
    elevation: 3,
  },
  ratingText: {
    color: '#fff',
    fontSize: 12,
    fontWeight: '700',
  },
  ratingTextActive: {
    color: '#001529',
    fontWeight: '800',
  },
  commentInput: {
    backgroundColor: 'rgba(255, 255, 255, 0.05)',
    borderWidth: 1,
    borderColor: 'rgba(255, 255, 255, 0.15)',
    borderRadius: 12,
    padding: 8,
    color: '#fff',
    fontSize: 13,
    minHeight: 55,
    textAlignVertical: 'top',
    marginBottom: 10,
    textAlign: I18nManager.isRTL ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  actionButtonsRow: {
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: 12,
    marginTop: 10,
    marginBottom: 30,
  },
  cancelBtn: {
    flex: 1,
    minHeight: 48,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.2)',
    backgroundColor: 'rgba(255,255,255,0.05)',
    justifyContent: 'center',
    alignItems: 'center',
    paddingHorizontal: 8,
    paddingVertical: 6,
  },
  cancelBtnText: {
    color: 'rgba(255,255,255,0.8)',
    fontSize: 13,
    fontWeight: '700',
    textAlign: 'center',
  },
  submitBtn: {
    flex: 2,
    minHeight: 48,
    borderRadius: 16,
  },
  submitBtnText: {
    fontSize: 13,
    textAlign: 'center',
    color: colors.bgDeep,
  },
  metricRemoveBtn: {
    width: 24,
    height: 24,
    borderRadius: 12,
    backgroundColor: 'rgba(239, 68, 68, 0.15)',
    borderWidth: 1,
    borderColor: 'rgba(239, 68, 68, 0.3)',
    justifyContent: 'center',
    alignItems: 'center',
    marginHorizontal: 8,
  },
  metricRemoveBtnText: {
    color: '#f87171',
    fontSize: 12,
    fontWeight: '900',
  },
  removedMetricsContainer: {
    marginTop: 8,
    marginBottom: 12,
  },
  removedMetricsTitle: {
    color: 'rgba(255, 255, 255, 0.5)',
    fontSize: 11,
    textAlign: I18nManager.isRTL ? 'left' : 'right',
    alignSelf: 'stretch',
    writingDirection: 'rtl',
    marginBottom: 6,
  },
  removedMetricsList: {
    flexWrap: 'wrap',
    gap: 6,
    marginTop: 6,
  },
  restoreChip: {
    backgroundColor: 'rgba(0, 212, 255, 0.1)',
    borderWidth: 1,
    borderColor: 'rgba(0, 212, 255, 0.25)',
    borderRadius: 12,
    paddingHorizontal: 8,
    paddingVertical: 4,
  },
  restoreChipText: {
    color: '#00d4ff',
    fontSize: 11,
    fontWeight: '700',
  },
});

