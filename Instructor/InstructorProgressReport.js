import React, { useRef, useEffect, useState } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  StatusBar, Easing, Platform, TextInput, Alert, findNodeHandle, Modal, FlatList, I18nManager,
  KeyboardAvoidingView, Keyboard,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
// DateTimePicker no longer needed – lesson dates are selected from a dropdown
import AppCard from '../components/ui/AppCard';
import { colors, shadows } from '../theme/tokens';
import PrimaryButton from '../components/ui/PrimaryButton';
import { API_BASE_URL } from '../apiConfig';

const { width, height } = Dimensions.get('window');
const AnimatedBlurView = Animated.createAnimatedComponent(BlurView);

// ========================================
// 1. ANIMATED WAVES
// ========================================
const Wave = ({ color, duration, startPosition, d }) => {
  const translateX = useRef(new Animated.Value(startPosition)).current;
  useEffect(() => {
    const toPosition = startPosition === 0 ? -600 : 0;
    Animated.loop(
      Animated.sequence([
        Animated.timing(translateX, { toValue: toPosition, duration: duration / 2, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
        Animated.timing(translateX, { toValue: startPosition, duration: duration / 2, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
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
// 3. PROGRESS BARS
// ========================================
const exerciseCatalog = [
  {
    key: 'front_float',
    title: 'ציפה על הבטן',
    metrics: [
      'הסתגלות וביטחון במים',
      'שליטה בנשימות (הכנסת ראש למים)',
      'תנועתיות וקואורדינציה',
      'יציבה וציפה',
      'תקשורת במים (ושיתוף פעולה)',
      'התמדה ומאמץ',
      'יוזמה',
      'קשב וריכוז',
      'תגובה להוראות',
      'עצמאות בתרגיל',
    ],
  },
  {
    key: 'back_float',
    title: 'ציפה על הגב',
    metrics: [
      'הסתגלות וביטחון במים',
      'שליטה בנשימות (הכנסת ראש למים)',
      'תנועתיות וקואורדינציה',
      'יציבה וציפה',
      'תקשורת במים (ושיתוף פעולה)',
      'התמדה ומאמץ',
      'יוזמה',
      'קשב וריכוז',
      'תגובה להוראות',
      'עצמאות בתרגיל',
    ],
  },
  {
    key: 'front_kicks',
    title: 'בעיטות בטן',
    metrics: [
      'הסתגלות וביטחון במים',
      'שליטה בנשימות (הכנסת ראש למים)',
      'תנועתיות וקואורדינציה',
      'יציבה וציפה',
      'תקשורת במים (ושיתוף פעולה)',
      'התמדה ומאמץ',
      'יוזמה',
      'קשב וריכוז',
      'תגובה להוראות',
      'עצמאות בתרגיל',
    ],
  },
  {
    key: 'back_kicks',
    title: 'בעיטות גב',
    metrics: [
      'הסתגלות וביטחון במים',
      'שליטה בנשימות (הכנסת ראש למים)',
      'תנועתיות וקואורדינציה',
      'יציבה וציפה',
      'תקשורת במים (ושיתוף פעולה)',
      'התמדה ומאמץ',
      'יוזמה',
      'קשב וריכוז',
      'תגובה להוראות',
      'עצמאות בתרגיל',
    ],
  },
  {
    key: 'arrow_jump',
    title: 'קפיצה חץ',
    metrics: [
      'הסתגלות וביטחון במים',
      'שליטה בנשימות (הכנסת ראש למים)',
      'תנועתיות וקואורדינציה',
      'יציבה וציפה',
      'תקשורת במים (ושיתוף פעולה)',
      'התמדה ומאמץ',
      'יוזמה',
      'קשב וריכוז',
      'תגובה להוראות',
      'עצמאות בתרגיל',
    ],
  },
  {
    key: 'deep_jump',
    title: 'קפיצה עמוק',
    metrics: [
      'הסתגלות וביטחון במים',
      'שליטה בנשימות (הכנסת ראש למים)',
      'תנועתיות וקואורדינציה',
      'יציבה וציפה',
      'תקשורת במים (ושיתוף פעולה)',
      'התמדה ומאמץ',
      'יוזמה',
      'קשב וריכוז',
      'תגובה להוראות',
      'עצמאות בתרגיל',
    ],
  },
  {
    key: 'hoop_pass',
    title: 'חישוק',
    metrics: [
      'הסתגלות וביטחון במים',
      'שליטה בנשימות (הכנסת ראש למים)',
      'תנועתיות וקואורדינציה',
      'יציבה וציפה',
      'תקשורת במים (ושיתוף פעולה)',
      'התמדה ומאמץ',
      'יוזמה',
      'קשב וריכוז',
      'תגובה להוראות',
      'עצמאות בתרגיל',
    ],
  },
  {
    key: 'water_confidence',
    title: 'ביטחון במים',
    metrics: [
      'הסתגלות וביטחון במים',
      'שליטה בנשימות (הכנסת ראש למים)',
      'תנועתיות וקואורדינציה',
      'יציבה וציפה',
      'תקשורת במים (ושיתוף פעולה)',
      'התמדה ומאמץ',
      'יוזמה',
      'קשב וריכוז',
      'תגובה להוראות',
      'עצמאות בתרגיל',
    ],
  },
  {
    key: 'breathing_control',
    title: 'שליטה בנשימות (הכנסת ראש למים)',
    metrics: [
      'הסתגלות וביטחון במים',
      'שליטה בנשימות (הכנסת ראש למים)',
      'תנועתיות וקואורדינציה',
      'יציבה וציפה',
      'תקשורת במים (ושיתוף פעולה)',
      'התמדה ומאמץ',
      'יוזמה',
      'קשב וריכוז',
      'תגובה להוראות',
      'עצמאות בתרגיל',
    ],
  },
];

const exerciseCatalogByKey = exerciseCatalog.reduce((acc, exercise) => {
  acc[exercise.key] = exercise;
  return acc;
}, {});

const createEmptyExerciseMetrics = (exerciseKey) => {
  const template = exerciseCatalogByKey[exerciseKey] || exerciseCatalog[0];
  return (template?.metrics || []).map((label) => ({
    label,
    value: RATING_MIN,
    isIncluded: true,
  }));
};

const createExerciseProgressStore = () => {
  return exerciseCatalog.reduce((acc, exercise) => {
    acc[exercise.key] = {
      exerciseKey: exercise.key,
      title: exercise.title,
      metrics: createEmptyExerciseMetrics(exercise.key),
    };
    return acc;
  }, {});
};

const getExerciseTitleByKey = (exerciseKey) => {
  const found = exerciseCatalogByKey[exerciseKey];
  return found?.title || 'תרגיל';
};

const getExerciseCompletionPercent = (metrics) => {
  const values = Array.isArray(metrics)
    ? metrics
      .filter((metric) => metric?.isIncluded !== false)
      .map((metric) => normalizeProgressValue(metric?.value))
    : [];

  if (values.length === 0) {
    return 0;
  }

  const average = values.reduce((sum, value) => sum + value, 0) / values.length;
  return Math.max(0, Math.min(100, Math.round(((average - RATING_MIN) / (RATING_MAX - RATING_MIN)) * 100)));
};

const RATING_MIN = 0;
const RATING_MAX = 5;

const normalizeProgressValue = (rawValue) => {
  if (!Number.isFinite(rawValue)) return RATING_MIN;
  const clamped = Math.max(RATING_MIN, Math.min(RATING_MAX, rawValue));
  return Math.round(clamped);
};

const formatRatingValue = (rawValue) => {
  return `${normalizeProgressValue(rawValue)}`;
};

const mapKeyToExerciseTitle = {
  front_float: 'ציפה על הבטן',
  back_float: 'ציפה על הגב',
  front_kicks: 'בעיטות בטן',
  back_kicks: 'בעיטות גב',
  arrow_jump: 'קפיצה חץ',
  deep_jump: 'קפיצה עמוק',
  hoop_pass: 'חישוק',
  water_confidence: 'ביטחון במים',
};

const getExerciseTitle = (item) => {
  const directTitle = String(item?.title || '').trim();
  if (directTitle) return directTitle;

  const key = String(item?.exerciseKey || '').trim();
  if (!key) return 'תרגיל';
  return mapKeyToExerciseTitle[key] || key;
};

const formatReportDate = (dateValue) => {
  if (!dateValue) return '';
  let d = dateValue;
  if (typeof dateValue === 'string') {
    const parts = dateValue.split('-');
    if (parts.length === 3) {
      d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
    } else {
      d = new Date(dateValue);
    }
  }
  if (!(d instanceof Date) || Number.isNaN(d.getTime())) {
    return '';
  }

  return d.toLocaleDateString('he-IL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
};

const toIsoDateString = (dateValue) => {
  if (!(dateValue instanceof Date) || Number.isNaN(dateValue.getTime())) {
    return '';
  }

  const year = String(dateValue.getFullYear());
  const month = String(dateValue.getMonth() + 1).padStart(2, '0');
  const day = String(dateValue.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const resolveSummaryErrorMessage = (payload) => {
  const backendMessage = String(payload?.message || '').trim();
  const detailText = Array.isArray(payload?.details)
    ? payload.details.map((item) => String(item || '')).join(' | ')
    : '';
  const normalizedMessage = backendMessage.toLowerCase();
  const normalizedDetails = detailText.toLowerCase();

  if (!backendMessage) {
    return 'לא ניתן לייצר סיכום התקדמות כרגע.';
  }

  if (backendMessage.includes('Missing OpenAI API key')) {
    return 'לא הוגדר מפתח AI בשרת. יש להגדיר OpenAI:ApiKey או OPENAI_API_KEY ואז לנסות שוב.';
  }

  if (
    normalizedMessage.includes('all configured ai api keys failed')
    || normalizedMessage.includes('provider returned')
    || normalizedMessage.includes('rate limit')
    || normalizedMessage.includes('429')
    || normalizedDetails.includes('rate limit')
    || normalizedDetails.includes('429')
  ) {
    if (normalizedMessage.includes('429') || normalizedDetails.includes('429')) {
      return 'שירות ה-AI הגיע למגבלת שימוש זמנית (429). אפשר לנסות שוב בעוד כמה דקות.';
    }
    return 'שירות ה-AI לא זמין כרגע. אפשר לרענן ולנסות שוב בעוד כמה דקות.';
  }

  return backendMessage;
};

const resolveNumericId = (...candidates) => {
  for (let i = 0; i < candidates.length; i += 1) {
    const value = Number(candidates[i]);
    if (Number.isInteger(value) && value > 0) {
      return value;
    }
  }

  return 0;
};

const parseReportDateInput = (rawValue) => {
  const input = String(rawValue ?? '').trim();
  if (!input) return null;

  let year;
  let month;
  let day;

  const isoMatch = input.match(/^(\d{4})-(\d{1,2})-(\d{1,2})$/);
  if (isoMatch) {
    year = Number(isoMatch[1]);
    month = Number(isoMatch[2]);
    day = Number(isoMatch[3]);
  } else {
    const localMatch = input.match(/^(\d{1,2})[./-](\d{1,2})[./-](\d{4})$/);
    if (!localMatch) return null;

    day = Number(localMatch[1]);
    month = Number(localMatch[2]);
    year = Number(localMatch[3]);
  }

  if (!Number.isFinite(year) || !Number.isFinite(month) || !Number.isFinite(day)) return null;

  const candidate = new Date(year, month - 1, day);
  if (candidate.getFullYear() !== year || candidate.getMonth() !== month - 1 || candidate.getDate() !== day) {
    return null;
  }

  return candidate;
};

const ReadOnlyBar = ({ label, value }) => {
  const safeVal = normalizeProgressValue(value || RATING_MIN);
  const fillPercentage = ((safeVal - RATING_MIN) / (RATING_MAX - RATING_MIN)) * 100;

  return (
    <View style={styles.metricRow}>
      <View style={[styles.metricLabelRow, { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' }]}>
        {Platform.OS === 'web' ? (
          <>
            <Text style={styles.metricPercent}>{formatRatingValue(safeVal)}/5</Text>
            <Text style={[styles.metricLabel, { textAlign: 'right', writingDirection: 'rtl', flexShrink: 1 }]}>{label}</Text>
          </>
        ) : (
          <>
            <Text style={[styles.metricLabel, { textAlign: 'right', writingDirection: 'rtl', flexShrink: 1 }]}>{label}</Text>
            <Text style={styles.metricPercent}>{formatRatingValue(safeVal)}/5</Text>
          </>
        )}
      </View>
      <View style={[styles.trackOuter, { flexDirection: 'row' }]}>
        <LinearGradient
          colors={['#00d4ff', '#0099cc']}
          start={{ x: 0, y: 0 }} end={{ x: 1, y: 0 }}
          style={[styles.trackFill, { width: `${fillPercentage}%` }]}
        />
      </View>
    </View>
  );
};

const WritableBar = ({ label, value, onChange, onInteractionStart, onInteractionEnd, disabled = false, onRemove }) => {
  const [trackWidth, setTrackWidth] = useState(0);
  const trackWidthRef = React.useRef(0);

  const handleTrackLayout = (event) => {
    const measuredWidth = Math.max(0, Number(event?.nativeEvent?.layout?.width ?? 0));
    setTrackWidth(measuredWidth);
    trackWidthRef.current = measuredWidth;
  };

  const updateValueFromEvent = (event) => {
    if (disabled) return;

    const measuredWidth = trackWidthRef.current;
    if (measuredWidth < 2) return;

    const touchX = Number(event?.nativeEvent?.locationX ?? 0);
    let ratio = (touchX + 14) / (measuredWidth + 28);
    ratio = Math.max(0, Math.min(1, ratio));
    const nextValue = normalizeProgressValue(RATING_MIN + ratio * (RATING_MAX - RATING_MIN));
    onChange(nextValue);
  };

  const safeVal = normalizeProgressValue(value);
  const fillPercentage = ((safeVal - RATING_MIN) / (RATING_MAX - RATING_MIN)) * 100;

  return (
    <View style={styles.metricRow}>
      <View style={[styles.metricLabelRow, { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' }]}>
        {Platform.OS === 'web' ? (
          <>
            <Text style={[styles.metricPercent, disabled ? styles.metricPercentDisabled : { color: '#00d4ff' }]}>{formatRatingValue(safeVal)}/5</Text>
            <View style={[styles.metricLabelActionsRow, { flexDirection: 'row', alignItems: 'center', justifyContent: 'flex-end', flexShrink: 1, gap: 8 }]}>
              <Text style={[styles.metricLabel, disabled && styles.metricLabelDisabled, { textAlign: 'right', writingDirection: 'rtl' }]}>{label}</Text>
              {typeof onRemove === 'function' ? (
                <TouchableOpacity
                  activeOpacity={0.8}
                  onPress={onRemove}
                  disabled={disabled}
                  style={[styles.metricRemoveButton, disabled && styles.metricRemoveButtonDisabled]}
                >
                  <Text style={[styles.metricRemoveButtonText, disabled && styles.metricRemoveButtonTextDisabled]}>➖</Text>
                </TouchableOpacity>
              ) : null}
            </View>
          </>
        ) : (
          <>
            <View style={[styles.metricLabelActionsRow, { flexDirection: 'row', alignItems: 'center', justifyContent: 'flex-end', flexShrink: 1, gap: 8 }]}>
              {typeof onRemove === 'function' ? (
                <TouchableOpacity
                  activeOpacity={0.8}
                  onPress={onRemove}
                  disabled={disabled}
                  style={[styles.metricRemoveButton, disabled && styles.metricRemoveButtonDisabled]}
                >
                  <Text style={[styles.metricRemoveButtonText, disabled && styles.metricRemoveButtonTextDisabled]}>➖</Text>
                </TouchableOpacity>
              ) : null}
              <Text style={[styles.metricLabel, disabled && styles.metricLabelDisabled, { textAlign: 'right', writingDirection: 'rtl' }]}>{label}</Text>
            </View>
            <Text style={[styles.metricPercent, disabled ? styles.metricPercentDisabled : { color: '#00d4ff' }]}>{formatRatingValue(safeVal)}/5</Text>
          </>
        )}
      </View>
      <View
        style={styles.trackTouchZone}
        onLayout={handleTrackLayout}
        onStartShouldSetResponder={() => !disabled}
        onMoveShouldSetResponder={() => !disabled}
        onResponderGrant={(event) => {
          if (disabled) return;
          onInteractionStart?.();
          updateValueFromEvent(event);
        }}
        onResponderMove={updateValueFromEvent}
        onResponderRelease={() => onInteractionEnd?.()}
        onResponderTerminate={() => onInteractionEnd?.()}
        hitSlop={{ top: 12, bottom: 12, left: 24, right: 24 }}
      >
        <View style={[styles.trackOuterWritable, { flexDirection: 'row' }, disabled && styles.trackOuterWritableDisabled]} pointerEvents="none">
          <LinearGradient
            colors={disabled ? ['#7d8798', '#677182'] : ['#00d4ff', '#0099cc']}
            start={{ x: 0, y: 0 }} end={{ x: 1, y: 0 }}
            style={[styles.trackFill, disabled && styles.trackFillDisabled, { width: `${fillPercentage}%` }]}
          />
        </View>
        {/* Glowing Thumb */}
        <View pointerEvents="none" style={[styles.thumbOuter, disabled && styles.thumbOuterDisabled, { left: -4 + (fillPercentage / 100) * (trackWidth + 8) }]}>
          <LinearGradient colors={disabled ? ['#7d8798', '#677182'] : ['#00d4ff', '#0099cc']} style={styles.thumbInner} />
        </View>
      </View>
    </View>
  );
};

// ========================================
// 4. MAIN COMPONENT
// ========================================
export default function InstructorProgressReport({ navigation, route }) {
  const scrollY = useRef(new Animated.Value(0)).current;
  const [mode, setMode] = useState('writable');
  const [exerciseStore, setExerciseStore] = useState(() => createExerciseProgressStore());
  const [exerciseDbStats, setExerciseDbStats] = useState({});
  const [selectedExerciseKey, setSelectedExerciseKey] = useState(exerciseCatalog[0].key);
  const [planRecommendation, setPlanRecommendation] = useState(null);
  const [isLoadingPlanRecommendation, setIsLoadingPlanRecommendation] = useState(false);
  const [planRecommendationError, setPlanRecommendationError] = useState('');
  const [readableAiSummary, setReadableAiSummary] = useState('');
  const [readableAiSummarySource, setReadableAiSummarySource] = useState('');
  const [isLoadingReadableSummary, setIsLoadingReadableSummary] = useState(false);
  const [readableSummaryError, setReadableSummaryError] = useState('');
  const [notes, setNotes] = useState('');
  const [isPresent, setIsPresent] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isMetricInteractionActive, setIsMetricInteractionActive] = useState(false);
  const [reportDate, setReportDate] = useState(new Date());
  const [availableLessonDates, setAvailableLessonDates] = useState([]);
  const [isLessonDatesDropdownOpen, setIsLessonDatesDropdownOpen] = useState(false);
  const [notesCardY, setNotesCardY] = useState(0);
  const scrollViewRef = useRef(null);
  const notesInputRef = useRef(null);
  const pastReportsRef = useRef([]);

  const [isKeyboardVisible, setIsKeyboardVisible] = useState(false);

  useEffect(() => {
    const keyboardDidShowListener = Keyboard.addListener(
      'keyboardDidShow',
      () => setIsKeyboardVisible(true)
    );
    const keyboardDidHideListener = Keyboard.addListener(
      'keyboardDidHide',
      () => setIsKeyboardVisible(false)
    );

    return () => {
      keyboardDidShowListener.remove();
      keyboardDidHideListener.remove();
    };
  }, []);

  const child = route?.params?.child || {};
  const authUser = route?.params?.authUser || {};
  const childId = resolveNumericId(route?.params?.childId, child?.childId, child?.id);
  const instructorId = resolveNumericId(route?.params?.instructorId, authUser?.id, child?.instructorId);
  const groupId = resolveNumericId(route?.params?.groupId, child?.groupId);
  const childName = child.name || 'ילד לא ידוע';
  const initial = childName[0] || 'א';
  const isAttendanceAbsent = mode === 'writable' && !isPresent;
  const selectedExercise = exerciseStore[selectedExerciseKey] || exerciseStore[exerciseCatalog[0].key];
  const metrics = selectedExercise?.metrics || [];

  const surfaceOpacity = scrollY.interpolate({ inputRange: [0, height * 0.8], outputRange: [1, 0], extrapolate: 'clamp' });
  const navBg = scrollY.interpolate({ inputRange: [0, 50], outputRange: ['rgba(0, 21, 41, 0.4)', 'rgba(0, 21, 41, 0.85)'], extrapolate: 'clamp' });

  const updateMetric = (idx, newValue) => {
    setExerciseStore((prevStore) => {
      const nextStore = { ...prevStore };
      const currentExercise = nextStore[selectedExerciseKey];

      if (!currentExercise?.metrics?.[idx] || currentExercise.metrics[idx].isIncluded === false) {
        return prevStore;
      }

      const nextMetrics = [...currentExercise.metrics];
      nextMetrics[idx] = {
        ...nextMetrics[idx],
        value: normalizeProgressValue(newValue),
      };

      nextStore[selectedExerciseKey] = {
        ...currentExercise,
        metrics: nextMetrics,
      };

      return nextStore;
    });
  };

  const setMetricIncluded = (idx, isIncluded) => {
    setExerciseStore((prevStore) => {
      const nextStore = { ...prevStore };
      const currentExercise = nextStore[selectedExerciseKey];

      if (!currentExercise?.metrics?.[idx]) {
        return prevStore;
      }

      const nextMetrics = [...currentExercise.metrics];
      nextMetrics[idx] = {
        ...nextMetrics[idx],
        isIncluded,
      };

      nextStore[selectedExerciseKey] = {
        ...currentExercise,
        metrics: nextMetrics,
      };

      return nextStore;
    });
  };

  const selectExercise = (exerciseKey) => {
    if (!exerciseCatalogByKey[exerciseKey]) {
      return;
    }

    setSelectedExerciseKey(exerciseKey);
  };

  useEffect(() => {
    if (mode !== 'writable') {
      setIsMetricInteractionActive(false);
    }
  }, [mode]);

  useEffect(() => {
    if (isAttendanceAbsent) {
      setIsMetricInteractionActive(false);
    }
  }, [isAttendanceAbsent]);

  // ── Fetch available lesson dates for this child ──
  useEffect(() => {
    if (!instructorId || !childId) return;
    let isCancelled = false;

    const loadLessonDates = async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/child/${childId}/lesson-dates`);
        const payload = await response.json().catch(() => null);
        if (!response.ok || !Array.isArray(payload) || isCancelled) return;

        if (!isCancelled) {
          setAvailableLessonDates(payload);
          if (payload.length > 0 && payload[0]?.date) {
            setReportDate(new Date(payload[0].date + 'T00:00:00'));
          }
        }
      } catch (_e) { /* silently ignore */ }
    };

    loadLessonDates();
    return () => { isCancelled = true; };
  }, [instructorId, childId]);

  // ── Fetch past reports from the DB and compute historical exercise percentages ──
  useEffect(() => {
    if (!childId || !instructorId) return;
    let isCancelled = false;

    const loadPastReports = async () => {
      try {
        const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children/${childId}/reports`);
        const payload = await response.json().catch(() => null);
        if (!response.ok || !Array.isArray(payload)) return;

        // Store raw reports for duplicate detection
        pastReportsRef.current = payload;

        // Group all present reports by exerciseKey and compute average percent per exercise
        const exerciseReportValues = {};
        payload.forEach((report) => {
          const key = String(report?.exerciseKey || '').trim();
          if (!key) return;
          // Skip absent reports
          if (report?.isPresent === false || report?.isPresent === 0) return;

          // Parse metrics
          let metricsArr = [];
          if (Array.isArray(report?.metrics)) {
            metricsArr = report.metrics;
          } else if (typeof report?.metrics === 'string' && report.metrics.trim()) {
            try { metricsArr = JSON.parse(report.metrics); } catch (_e) { }
          }
          if (!Array.isArray(metricsArr) || metricsArr.length === 0) return;

          // Compute average metric value for this report (0-10 scale)
          const values = metricsArr
            .map((m) => Number(m?.value))
            .filter((v) => Number.isFinite(v));
          if (values.length === 0) return;

          // Divide by the total possible metrics for this exercise, not just the reported ones (usually 10)
          const totalPossibleMetrics = exerciseCatalogByKey[key]?.metrics?.length || 10;
          const sumOfRatings = values.reduce((s, v) => s + v, 0);
          const avgForReport = sumOfRatings / totalPossibleMetrics;

          if (!exerciseReportValues[key]) exerciseReportValues[key] = [];
          exerciseReportValues[key].push(avgForReport);
        });

        if (isCancelled) return;

        // Compute stats per exercise: percent (0-100) and report count
        const stats = {};
        Object.keys(exerciseReportValues).forEach((key) => {
          const allAvgs = exerciseReportValues[key];
          const overallAvg = allAvgs.reduce((s, v) => s + v, 0) / allAvgs.length;
          stats[key] = {
            percent: Math.max(0, Math.min(100, Math.round((overallAvg / RATING_MAX) * 100))),
            avgSum: allAvgs.reduce((s, v) => s + v, 0),
            reportCount: allAvgs.length,
          };
        });

        if (!isCancelled) {
          setExerciseDbStats(stats);
        }
      } catch (_err) {
        // silently fail — percents stay at 0
      }
    };

    loadPastReports();
    return () => { isCancelled = true; };
  }, [childId, instructorId]);

  // ── Auto-load existing child report for the selected exercise and date ──
  useEffect(() => {
    if (!reportDate) return;
    const reportDateStr = toIsoDateString(reportDate).trim();

    // 1. Find if there is any report for the selected date
    const reportForDate = (pastReportsRef.current || []).find(
      (r) => String(r?.reportDate || '').trim() === reportDateStr
    );

    // 2. If a report exists for this date under a different exercise, switch to it
    if (reportForDate && reportForDate.exerciseKey && String(reportForDate.exerciseKey).trim() !== selectedExerciseKey) {
      setSelectedExerciseKey(String(reportForDate.exerciseKey).trim());
      return;
    }

    // 3. Now verify if we have a report matching the selected exercise & date
    const existing = (pastReportsRef.current || []).find(
      (r) =>
        String(r?.reportDate || '').trim() === reportDateStr &&
        String(r?.exerciseKey || '').trim() === selectedExerciseKey
    );

    if (existing) {
      setIsPresent(existing.isPresent !== false);
      setNotes(existing.comment || '');

      let metricsArr = [];
      if (Array.isArray(existing.metrics)) {
        metricsArr = existing.metrics;
      } else if (typeof existing.metrics === 'string' && existing.metrics.trim()) {
        try {
          metricsArr = JSON.parse(existing.metrics);
        } catch (_e) { }
      }

      setExerciseStore((prevStore) => {
        const nextStore = { ...prevStore };
        const currentExercise = nextStore[selectedExerciseKey];
        if (!currentExercise) return prevStore;

        const template = exerciseCatalogByKey[selectedExerciseKey];
        const nextMetrics = (template?.metrics || []).map((label) => {
          const matched = metricsArr.find(
            (m) => String(m?.label || '').trim() === label.trim()
          );
          if (matched && mode !== 'writable') {
            return {
              label,
              value: normalizeProgressValue(matched.value),
              isIncluded: matched.isIncluded !== false,
            };
          } else {
            const wasExcluded = metricsArr.length > 0;
            return {
              label,
              value: RATING_MIN,
              isIncluded: matched ? matched.isIncluded !== false : !wasExcluded,
            };
          }
        });

        nextStore[selectedExerciseKey] = {
          ...currentExercise,
          metrics: nextMetrics,
        };
        return nextStore;
      });
    } else {
      setIsPresent(true);
      setNotes('');
      setExerciseStore((prevStore) => {
        const nextStore = { ...prevStore };
        const currentExercise = nextStore[selectedExerciseKey];
        if (!currentExercise) return prevStore;

        nextStore[selectedExerciseKey] = {
          ...currentExercise,
          metrics: createEmptyExerciseMetrics(selectedExerciseKey),
        };
        return nextStore;
      });
    }
  }, [selectedExerciseKey, reportDate, exerciseDbStats, mode]);

  useEffect(() => {
    if (mode !== 'readable') {
      return;
    }

    if (!childId || !instructorId) {
      setPlanRecommendation(null);
      setPlanRecommendationError('לא ניתן לייצר תכנית עבודה ללא מזהה ילד ומדריך.');
      setIsLoadingPlanRecommendation(false);
      return;
    }

    let isCancelled = false;

    const loadPlanRecommendation = async () => {
      try {
        setIsLoadingPlanRecommendation(true);
        setPlanRecommendationError('');

        const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children/${childId}/exercise-plan/recommendation`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ includeCompleted: true }),
        });

        const payload = await response.json().catch(() => null);
        if (!response.ok) {
          throw new Error(payload?.message || 'לא ניתן לטעון תכנית תרגול אישית כרגע.');
        }

        if (!isCancelled) {
          setPlanRecommendation(payload || null);
        }
      } catch (error) {
        if (!isCancelled) {
          setPlanRecommendation(null);
          setPlanRecommendationError(error?.message || 'לא ניתן לטעון תכנית תרגול אישית כרגע.');
        }
      } finally {
        if (!isCancelled) {
          setIsLoadingPlanRecommendation(false);
        }
      }
    };

    loadPlanRecommendation();

    return () => {
      isCancelled = true;
    };
  }, [mode, childId, instructorId]);

  useEffect(() => {
    if (mode !== 'readable') {
      return;
    }

    if (!childId || !instructorId) {
      setReadableAiSummary('');
      setReadableAiSummarySource('');
      setReadableSummaryError('לא ניתן לייצר סיכום ללא מזהה ילד ומדריך.');
      setIsLoadingReadableSummary(false);
      return;
    }

    let isCancelled = false;

    const loadReadableSummary = async () => {
      try {
        setIsLoadingReadableSummary(true);
        setReadableSummaryError('');

        // Step 1: start the job
        const startResponse = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children/${childId}/reports/summary`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
        });
        const startPayload = await startResponse.json().catch(() => null);

        if (!startResponse.ok) {
          throw new Error(resolveSummaryErrorMessage(startPayload));
        }

        // Immediate result (no reports / fallback)
        if (startPayload?.status === 'done') {
          if (!isCancelled) {
            setReadableAiSummary(String(startPayload?.summary || '').trim());
            setReadableAiSummarySource(String(startPayload?.source || '').trim());
          }
          return;
        }

        const jobId = startPayload?.jobId;
        if (!jobId) throw new Error('לא התקבל מזהה עבודה מהשרת.');

        // Step 2: poll every 3s until done or error
        const pollIntervalMs = 3000;
        const maxWaitMs = 120000;
        const startedAt = Date.now();

        while (!isCancelled) {
          await new Promise(resolve => setTimeout(resolve, pollIntervalMs));
          if (isCancelled) break;
          if (Date.now() - startedAt > maxWaitMs) {
            throw new Error('הסיכום לוקח יותר מדי זמן. נסו שוב מאוחר יותר.');
          }

          const pollResponse = await fetch(
            `${API_BASE_URL}/instructor/${instructorId}/children/${childId}/reports/summary/${jobId}`
          );
          const pollPayload = await pollResponse.json().catch(() => null);

          if (!pollResponse.ok) {
            throw new Error(resolveSummaryErrorMessage(pollPayload));
          }

          if (pollPayload?.status === 'done') {
            if (!isCancelled) {
              setReadableAiSummary(String(pollPayload?.summary || '').trim());
              setReadableAiSummarySource(String(pollPayload?.source || '').trim());
            }
            return;
          }

          if (pollPayload?.status === 'error') {
            throw new Error(pollPayload?.message || 'שירות ה-AI נכשל.');
          }
          // status === 'pending' → keep polling
        }
      } catch (error) {
        if (!isCancelled) {
          setReadableAiSummary('');
          setReadableAiSummarySource('');
          setReadableSummaryError(error?.message || 'לא ניתן לייצר סיכום התקדמות כרגע.');
        }
      } finally {
        if (!isCancelled) {
          setIsLoadingReadableSummary(false);
        }
      }
    };

    loadReadableSummary();

    return () => {
      isCancelled = true;
    };
  }, [mode, childId, instructorId]);

  const openReportDatePicker = () => {
    setIsLessonDatesDropdownOpen(true);
  };

  const selectLessonDate = (dateStr) => {
    setReportDate(new Date(dateStr + 'T00:00:00'));
    setIsLessonDatesDropdownOpen(false);
  };

  const scrollNotesInputAboveKeyboard = () => {
    const rawScrollRef = scrollViewRef.current;
    if (!rawScrollRef) {
      return;
    }

    const scrollRef = typeof rawScrollRef.getNode === 'function'
      ? rawScrollRef.getNode()
      : rawScrollRef;

    const notesInputHandle = findNodeHandle(notesInputRef.current);

    if (
      notesInputHandle
      && typeof scrollRef.scrollResponderScrollNativeHandleToKeyboard === 'function'
    ) {
      scrollRef.scrollResponderScrollNativeHandleToKeyboard(notesInputHandle, 110, true);
      return;
    }

    scrollRef?.scrollTo?.({
      y: Math.max(notesCardY - 140, 0),
      animated: true,
    });
  };

  const saveReport = async (forceOverwrite = false) => {
    if (isSaving) {
      return;
    }

    if (!childId || !instructorId) {
      Alert.alert('חסרים פרטים', 'לא ניתן לשמור דיווח בלי מזהה ילד ומזהה מדריך תקינים. חזרו לפרופיל הילד ונסו שוב.');
      return;
    }

    const reportDateStr = toIsoDateString(reportDate).trim();

    const hasDate = availableLessonDates.some(item => item?.date === reportDateStr);
    if (availableLessonDates.length > 0 && !hasDate) {
      Alert.alert('תאריך לא תקין', 'לא ניתן לדווח על התקדמות בתאריך שלא היה בו שיעור. אנא בחרו תאריך שיעור מהרשימה.');
      return;
    }

    const activeExercise = exerciseStore[selectedExerciseKey] || selectedExercise;
    const exerciseKey = String(activeExercise?.exerciseKey || activeExercise?.key || selectedExerciseKey).trim();
    const exerciseTitle = activeExercise?.title || getExerciseTitleByKey(selectedExerciseKey);

    const isPresentInMeeting = Boolean(isPresent);
    const normalizedMetrics = isPresentInMeeting
      ? metrics
        .filter((item) => item?.isIncluded !== false)
        .map((item) => ({
          label: String(item?.label || '').trim(),
          value: normalizeProgressValue(item?.value),
        }))
      : [];
    const normalizedComment = isPresentInMeeting ? notes.trim() : '';

    try {
      setIsSaving(true);

      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children/${childId}/reports`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          groupId: groupId || null,
          reportDate: reportDateStr,
          isPresent: isPresentInMeeting,
          comment: normalizedComment,
          metrics: normalizedMetrics,
          exerciseKey: exerciseKey,
          exerciseTitle: exerciseTitle,
          overwriteExisting: forceOverwrite || false,
        }),
      });

      const payload = await response.json().catch(() => null);

      // Backend returned 409 Conflict → a report already exists for this date+exercise
      if (response.status === 409 && payload?.message === 'duplicate_report') {
        setIsSaving(false);
        Alert.alert(
          '⚠️ דיווח קיים',
          `כבר קיים דיווח עבור התרגיל "${exerciseTitle}" בתאריך ${formatReportDate(reportDate)}.\n\nשמירה עכשיו תעדכן את הדיווח הקיים. האם להמשיך?`,
          [
            { text: 'ביטול', style: 'cancel' },
            { text: 'עדכן דיווח', style: 'default', onPress: () => saveReport(true) },
          ],
        );
        return;
      }

      if (!response.ok) {
        throw new Error(payload?.message || 'שמירת הדיווח נכשלה. נסו שוב.');
      }

      if (payload) {
        pastReportsRef.current = [...pastReportsRef.current.filter(r => r.reportId !== payload.reportId), payload];
      }

      Alert.alert('מעולה', 'הדיווח נשמר בהצלחה!');
      navigation.goBack();
    } catch (error) {
      Alert.alert('שגיאת שמירה', `${error?.message || 'לא ניתן לשמור כרגע את הדיווח.'}\n\n(נשלח: תאריך=${reportDateStr}, קבוצה=${groupId}, ילד=${childId}, מדריך=${instructorId})`);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <View style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="transparent" translucent={true} />

      {/* Deep Ocean */}
      <LinearGradient colors={['#005a80', '#00456a', colors.bgDeep]} style={StyleSheet.absoluteFill} />
      <Animated.View style={[StyleSheet.absoluteFill, { opacity: surfaceOpacity }]}>
        <LinearGradient colors={['#00aed8', '#007fa7', '#004d73']} style={{ flex: 1 }} />
      </Animated.View>

      {!isKeyboardVisible && (
        <>
          <Wave color="rgba(0, 153, 204, 0.3)" duration={8000} startPosition={0} d="M0,75 C200,45 400,105 600,75 C800,45 1000,105 1200,75 L1200,150 L0,150 Z M1200,75 C1400,45 1600,105 1800,75 C2000,45 2200,105 2400,75 L2400,150 L1200,150 Z" />
          <Wave color="rgba(0, 212, 255, 0.25)" duration={6000} startPosition={-600} d="M0,100 C300,60 500,130 800,90 C1000,60 1100,120 1200,100 L1200,150 L0,150 Z M1200,100 C1500,60 1700,130 2000,90 C2200,60 2300,120 2400,100 L2400,150 L1200,150 Z" />
        </>
      )}

      {/* NAVBAR */}
      <AnimatedBlurView intensity={20} tint="dark" style={[styles.navbar, { backgroundColor: navBg }]}>
        <TouchableOpacity onPress={() => navigation.goBack()} style={styles.backButton}>
          <Text style={styles.backButtonIcon}>{'\u203a'}</Text>
        </TouchableOpacity>
      </AnimatedBlurView>

      {/* SCROLLABLE CONTENT */}
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        style={{ flex: 1 }}
        keyboardVerticalOffset={Platform.OS === 'ios' ? 80 : 0}
      >
        <Animated.ScrollView
          ref={scrollViewRef}
          onScroll={Animated.event([{ nativeEvent: { contentOffset: { y: scrollY } } }], { useNativeDriver: false })}
          scrollEventThrottle={16}
          scrollEnabled={!isMetricInteractionActive}
          contentContainerStyle={styles.scrollContent}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
          keyboardDismissMode={Platform.OS === 'ios' ? 'interactive' : 'on-drag'}
        >
        {/* Page Title */}
        <View style={styles.pageHeader}>
          <View style={styles.avatarGlow}>
            <LinearGradient colors={['#00d4ff', '#0099cc']} style={styles.avatar}>
              <Text style={styles.avatarText}>{initial}</Text>
            </LinearGradient>
          </View>
          <Text style={styles.pageTitle}>דיווח התקדמות</Text>
          <Text style={styles.childNameLabel}>{childName}</Text>
        </View>

        {/* Mode Toggle */}
        <View style={styles.toggleRow}>
          <TouchableOpacity
            onPress={() => setMode('writable')}
            style={[styles.toggleBtn, mode === 'writable' && styles.toggleBtnActive]}
          >
            <Text style={[styles.toggleText, mode === 'writable' && styles.toggleTextActive]}>📝 דיווח פגישה</Text>
          </TouchableOpacity>
          <TouchableOpacity
            onPress={() => setMode('readable')}
            style={[styles.toggleBtn, mode === 'readable' && styles.toggleBtnActive]}
          >
            <Text style={[styles.toggleText, mode === 'readable' && styles.toggleTextActive]}>📌 סיכום ותכנית</Text>
          </TouchableOpacity>
        </View>

        {/* Info Strip */}
        <View style={styles.infoStrip}>
          {mode === 'readable' ? (
            <View>
              <Text style={styles.infoText}>📅 תאריכים: מתחילת השנה ועד היום</Text>
              {isLoadingPlanRecommendation ? (
                <Text style={[styles.infoText, styles.readableInfoMetaText]}>מחשב תכנית עבודה מותאמת...</Text>
              ) : planRecommendationError ? (
                <Text style={[styles.infoText, styles.readableInfoErrorText]}>{planRecommendationError}</Text>
              ) : null}
            </View>
          ) : (
            <View>
              <TouchableOpacity activeOpacity={0.8} onPress={openReportDatePicker} style={styles.datePickerTrigger}>
                <Text style={styles.infoText}>📅 תאריך דיווח: {formatReportDate(reportDate)} (לחץ לשינוי)</Text>
              </TouchableOpacity>

              <Modal
                visible={isLessonDatesDropdownOpen}
                transparent
                animationType="fade"
                onRequestClose={() => setIsLessonDatesDropdownOpen(false)}
              >
                <TouchableOpacity
                  style={styles.lessonDatesOverlay}
                  activeOpacity={1}
                  onPress={() => setIsLessonDatesDropdownOpen(false)}
                >
                  <View style={styles.lessonDatesModal}>
                    <Text style={styles.lessonDatesTitle}>בחר תאריך שיעור</Text>
                    <FlatList
                      data={availableLessonDates}
                      keyExtractor={(item) => item?.date || String(Math.random())}
                      style={styles.lessonDatesList}
                      ListEmptyComponent={
                        <Text style={{ textAlign: 'center', color: '#888', padding: 20, fontSize: 15 }}>
                          אין שיעורים מתוזמנים לילד זה
                        </Text>
                      }
                      renderItem={({ item }) => {
                        const isSelected = toIsoDateString(reportDate) === item?.date;
                        return (
                          <TouchableOpacity
                            style={[styles.lessonDateItem, isSelected && styles.lessonDateItemSelected]}
                            activeOpacity={0.7}
                            onPress={() => selectLessonDate(item?.date)}
                          >
                            <Text style={[styles.lessonDateText, isSelected && styles.lessonDateTextSelected]}>
                              {formatReportDate(item?.date)} - {item?.meetingType || 'אישי'}
                            </Text>
                          </TouchableOpacity>
                        );
                      }}
                    />
                    <TouchableOpacity
                      style={styles.lessonDatesCloseBtn}
                      activeOpacity={0.8}
                      onPress={() => setIsLessonDatesDropdownOpen(false)}
                    >
                      <Text style={styles.lessonDatesCloseBtnText}>ביטול</Text>
                    </TouchableOpacity>
                  </View>
                </TouchableOpacity>
              </Modal>
            </View>
          )}
        </View>

        {mode === 'writable' ? (
          <AppCard useBlur blurIntensity={25} blurTint="dark" style={styles.attendanceCard}>
            <Text style={styles.sectionTitle}>נוכחות</Text>
            <View style={styles.attendanceToggleRow}>
              <TouchableOpacity
                activeOpacity={0.85}
                onPress={() => setIsPresent(true)}
                style={[styles.attendanceOption, isPresent && styles.attendanceOptionPresentActive]}
              >
                <View style={styles.attendanceOptionInner}>
                  <Text style={styles.attendanceEmoji}>✅</Text>
                  <Text style={[styles.attendanceOptionText, isPresent && styles.attendanceOptionTextActive]}>נכח</Text>
                </View>
              </TouchableOpacity>
              <TouchableOpacity
                activeOpacity={0.85}
                onPress={() => setIsPresent(false)}
                style={[styles.attendanceOption, !isPresent && styles.attendanceOptionAbsentActive]}
              >
                <View style={styles.attendanceOptionInner}>
                  <Text style={styles.attendanceEmoji}>❌</Text>
                  <Text style={[styles.attendanceOptionText, !isPresent && styles.attendanceOptionTextActive]}>לא נכח</Text>
                </View>
              </TouchableOpacity>
            </View>
            <Text style={styles.attendanceHintText}>
              {isPresent
                ? 'כאשר הילד נכח בפגישה, ניתן לעדכן מדדים והערות מדריך.'
                : 'כאשר הילד לא נכח, המדדים וההערות נעולים ונשמר דיווח אי-נוכחות.'}
            </Text>
          </AppCard>
        ) : null}

        {/* Metrics Card */}
        <AppCard useBlur blurIntensity={25} blurTint="dark" style={[styles.metricsCard, isAttendanceAbsent && styles.disabledCard]}>
          <Text style={styles.sectionTitle}>{mode === 'readable' ? 'תכנית עבודה מותאמת לילד' : 'מדדי התקדמות'}</Text>
          {mode === 'readable' ? (
            <View style={styles.recommendationWrap}>
              {isLoadingPlanRecommendation ? (
                <Text style={styles.notesReadMetaText}>מייצר סדר תרגילים אישי...</Text>
              ) : planRecommendationError ? (
                <Text style={[styles.notesReadText, styles.notesReadErrorText]}>{planRecommendationError}</Text>
              ) : (
                <View>
                  <View style={{ marginBottom: 24 }}>
                    <View style={{ flexDirection: Platform.OS === 'web' ? 'row-reverse' : 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
                      <Text style={{ color: '#fff', fontSize: 16, fontWeight: '600', textAlign: Platform.OS === 'web' ? 'right' : 'left' }}>מעקב חשיפה לתרגילים</Text>
                      <Text style={{ color: '#00d4ff', fontSize: 16, fontWeight: '700' }}>{Number(planRecommendation?.coveragePercent || 0)}%</Text>
                    </View>
                    <View style={{ height: 8, backgroundColor: 'rgba(255,255,255,0.1)', borderRadius: 4, overflow: 'hidden', flexDirection: Platform.OS === 'web' ? 'row' : 'row-reverse' }}>
                      <LinearGradient
                        colors={['#00d4ff', '#0099cc']}
                        start={{ x: 0, y: 0 }} end={{ x: 1, y: 0 }}
                        style={{ width: `${Number(planRecommendation?.coveragePercent || 0)}%`, height: '100%' }}
                      />
                    </View>
                    <Text style={{ color: 'rgba(255,255,255,0.6)', fontSize: 13, marginTop: 6, textAlign: Platform.OS === 'web' ? 'right' : 'left' }}>
                      הילד נחשף ל-{Number(planRecommendation?.completedExercises || 0)} מתוך {Number(planRecommendation?.totalExercises || 0)} התרגילים הכלליים.
                    </Text>
                  </View>

                  <View style={[styles.nextExerciseBox, { borderColor: 'rgba(0, 212, 255, 0.4)', borderWidth: 1, backgroundColor: 'rgba(0,212,255,0.08)', borderRadius: 16, padding: 16, marginBottom: 28 }]}>
                    <View style={{ flexDirection: Platform.OS === 'web' ? 'row-reverse' : 'row', alignItems: 'center', marginBottom: 10, gap: 10 }}>
                      <Text style={{ fontSize: 24 }}>🎯</Text>
                      <Text style={{ color: '#00d4ff', fontSize: 18, fontWeight: 'bold' }}>היעד הבא המומלץ</Text>
                    </View>
                    <Text style={[styles.nextExerciseName, { fontSize: 20, marginBottom: 8, textAlign: Platform.OS === 'web' ? 'right' : 'left', color: '#fff' }]}>{getExerciseTitle(planRecommendation?.nextExercise)}</Text>
                    <Text style={[styles.nextExerciseReason, { color: 'rgba(255,255,255,0.8)', fontSize: 14, textAlign: Platform.OS === 'web' ? 'right' : 'left', lineHeight: 20 }]}>{String(planRecommendation?.nextExercise?.reason || 'ההמלצה מבוססת על הקשיים הנוכחיים והיסטוריית הדיווחים.').trim()}</Text>
                  </View>

                  <Text style={[styles.planListTitle, { fontSize: 18, fontWeight: 'bold', color: '#fff', marginBottom: 16, textAlign: Platform.OS === 'web' ? 'right' : 'left' }]}>📋 סדר עדיפויות להמשך עבודה</Text>
                  {(Array.isArray(planRecommendation?.orderedExercises) ? planRecommendation.orderedExercises : []).slice(0, 8).map((item, index) => (
                    <View 
                      key={`${item?.exerciseKey || index}`} 
                      style={[
                        styles.planRow, 
                        { 
                          backgroundColor: 'rgba(255, 255, 255, 0.06)', 
                          borderRadius: 12, 
                          padding: 12, 
                          marginBottom: 10, 
                          flexDirection: Platform.OS === 'web' ? 'row-reverse' : 'row', 
                          justifyContent: 'space-between', 
                          alignItems: 'center', 
                          columnGap: 12
                        }
                      ]}
                    >
                      {/* Badge (always visual right) */}
                      <View style={{ width: 30, height: 30, borderRadius: 15, backgroundColor: index === 0 ? '#00d4ff' : 'rgba(255,255,255,0.15)', justifyContent: 'center', alignItems: 'center', flexShrink: 0 }}>
                        <Text style={{ color: index === 0 ? '#000' : '#fff', fontWeight: 'bold', fontSize: 14 }}>{index + 1}</Text>
                      </View>

                      {/* Text Title (always visual middle) */}
                      <Text 
                        style={[
                          styles.planRowText, 
                          { 
                            color: '#fff', 
                            fontSize: 16, 
                            fontWeight: '600', 
                            flex: 1, 
                            textAlign: Platform.OS === 'web' ? 'right' : 'left' 
                          }
                        ]} 
                        numberOfLines={2}
                      >
                        {getExerciseTitle(item)}
                      </Text>

                      {/* Meta Pill (always visual left) */}
                      <View style={{ backgroundColor: item?.completedCount > 0 ? 'rgba(0, 212, 255, 0.15)' : 'rgba(255, 255, 255, 0.1)', paddingHorizontal: 10, paddingVertical: 4, borderRadius: 12, flexShrink: 0 }}>
                        <Text style={[styles.planRowMeta, { color: item?.completedCount > 0 ? '#00d4ff' : 'rgba(255,255,255,0.7)', fontSize: 12, fontWeight: '600' }]} numberOfLines={1}>
                          {item?.completedCount > 0 ? `תורגל ${item.completedCount} פעמים` : 'טרם תורגל'}
                        </Text>
                      </View>
                    </View>
                  ))}
                </View>
              )}
            </View>
          ) : (
            <View>
              <Text style={styles.exercisePickerTitle}>בחר תרגיל פעיל</Text>
              <View style={styles.exercisePickerWrap}>
                {exerciseCatalog.map((exercise) => {
                  const dbStat = exerciseDbStats[exercise.key];
                  const dbPercent = dbStat?.percent || 0;
                  const dbCount = dbStat?.reportCount || 0;
                  const dbAvgSum = dbStat?.avgSum || 0;

                  // Blend current slider values into the historical average
                  const currentMetrics = exerciseStore[exercise.key]?.metrics || [];
                  const activeMetrics = currentMetrics.filter((m) => m?.isIncluded !== false);
                  const hasCurrentInput = activeMetrics.some((m) => m?.value > 0);

                  let exercisePercent = dbPercent;
                  if (hasCurrentInput && activeMetrics.length > 0) {
                    const totalPossibleMetrics = exercise.metrics?.length || 10;
                    const sumOfRatings = activeMetrics.reduce((s, m) => s + normalizeProgressValue(m?.value), 0);
                    const currentAvg = sumOfRatings / totalPossibleMetrics;
                    const blendedAvg = (dbAvgSum + currentAvg) / (dbCount + 1);
                    exercisePercent = Math.max(0, Math.min(100, Math.round((blendedAvg / RATING_MAX) * 100)));
                  }

                  const isSelected = selectedExerciseKey === exercise.key;

                  return (
                    <TouchableOpacity
                      key={exercise.key}
                      activeOpacity={0.9}
                      onPress={() => selectExercise(exercise.key)}
                      style={[styles.exerciseChip, isSelected && styles.exerciseChipActive]}
                    >
                      <Text style={[styles.exerciseChipPercent, isSelected && styles.exerciseChipPercentActive]}>{exercisePercent}%</Text>
                      <Text style={[styles.exerciseChipText, isSelected && styles.exerciseChipTextActive]}>{exercise.title}</Text>
                    </TouchableOpacity>
                  );
                })}
              </View>

              <View style={styles.selectedExerciseBox}>
                <Text style={styles.selectedExerciseLabel}>התרגיל הנוכחי</Text>
                <Text style={styles.selectedExerciseTitle}>{selectedExercise?.title || getExerciseTitleByKey(selectedExerciseKey)}</Text>
                <Text style={styles.selectedExerciseMeta}>
                  התקדמות: {(() => {
                    const stat = exerciseDbStats[selectedExerciseKey];
                    const dbP = stat?.percent || 0;
                    const dbC = stat?.reportCount || 0;
                    const dbS = stat?.avgSum || 0;
                    const active = metrics.filter((m) => m?.isIncluded !== false);
                    const touched = active.some((m) => m?.value > 0);
                    if (touched && active.length > 0) {
                      const totalPossibleMetrics = exerciseCatalogByKey[selectedExerciseKey]?.metrics?.length || 10;
                      const sumOfRatings = active.reduce((s, m) => s + normalizeProgressValue(m?.value), 0);
                      const curAvg = sumOfRatings / totalPossibleMetrics;
                      return Math.max(0, Math.min(100, Math.round(((dbS + curAvg) / (dbC + 1) / RATING_MAX) * 100)));
                    }
                    return dbP;
                  })()}% | מדדים: {metrics.length}
                </Text>
                <TouchableOpacity
                  activeOpacity={0.85}
                  onPress={() => setSelectedExerciseKey((prev) => prev)}
                  style={styles.selectedExerciseHint}
                >
                  <Text style={styles.selectedExerciseHintText}>לחץ על כרטיס אחר כדי לעבור לתרגיל אחר</Text>
                </TouchableOpacity>
              </View>

              {metrics.every((item) => item?.isIncluded === false) ? (
                <Text style={styles.noMetricsText}>לא נבחרו מדדים לתרגיל הנוכחי.</Text>
              ) : null}

              {metrics.map((item, idx) => {
                if (item?.isIncluded === false) {
                  return null;
                }

                return (
                  <WritableBar
                    key={`${selectedExerciseKey}-${item.label}`}
                    label={item.label}
                    value={item.value}
                    onChange={(nv) => updateMetric(idx, nv)}
                    onInteractionStart={() => setIsMetricInteractionActive(true)}
                    onInteractionEnd={() => setIsMetricInteractionActive(false)}
                    onRemove={() => setMetricIncluded(idx, false)}
                    disabled={isAttendanceAbsent}
                  />
                );
              })}

              {metrics.some((item) => item?.isIncluded === false) ? (
                <View style={styles.removedMetricsWrap}>
                  <Text style={styles.removedMetricsTitle}>מדדים שהוסרו מהתרגיל הזה (לחץ להחזרה):</Text>
                  <View style={styles.removedMetricsList}>
                    {metrics.map((item, idx) => {
                      if (item?.isIncluded !== false) {
                        return null;
                      }

                      return (
                        <TouchableOpacity
                          key={`restore-${selectedExerciseKey}-${item.label}`}
                          activeOpacity={0.85}
                          onPress={() => setMetricIncluded(idx, true)}
                          disabled={isAttendanceAbsent}
                          style={[styles.restoreMetricChip, isAttendanceAbsent && styles.restoreMetricChipDisabled]}
                        >
                          <Text style={[styles.restoreMetricChipText, isAttendanceAbsent && styles.restoreMetricChipTextDisabled]}>{`↩ ${item.label}`}</Text>
                        </TouchableOpacity>
                      );
                    })}
                  </View>
                </View>
              ) : null}
            </View>
          )}
        </AppCard>

        {/* Notes Card */}
        <View
          onLayout={(event) => {
            const nextY = Number(event?.nativeEvent?.layout?.y || 0);
            if (Number.isFinite(nextY) && nextY >= 0) {
              setNotesCardY(nextY);
            }
          }}
        >
          <AppCard useBlur blurIntensity={25} blurTint="dark" style={[styles.notesCard, isAttendanceAbsent && styles.disabledCard]}>
            <Text style={styles.sectionTitle}>
              {mode === 'readable' ? 'סיכום של הבינה המלאכותית' : 'הערות של המדריך (אופציונלי)'}
            </Text>
            {mode === 'readable' ? (
              <View style={styles.notesReadBox}>
                {isLoadingReadableSummary ? (
                  <Text style={styles.notesReadMetaText}>מייצר סיכום AI מכל הדיווחים של הילד...</Text>
                ) : readableSummaryError ? (
                  <Text style={[styles.notesReadText, styles.notesReadErrorText]}>{readableSummaryError}</Text>
                ) : (
                  <View>
                    <Text style={styles.notesReadText}>{readableAiSummary}</Text>
                    <Text style={styles.notesReadSourceText}>
                      {readableAiSummarySource === 'ai'
                        ? 'הסיכום נוצר אוטומטית בעזרת AI לפי כלל הדיווחים ההיסטוריים.'
                        : 'הסיכום נוצר מנתוני הדיווחים הקיימים.'}
                    </Text>
                  </View>
                )}
              </View>
            ) : (
              <TextInput
                style={[styles.notesInput, isAttendanceAbsent && styles.notesInputDisabled]}
                value={notes}
                onChangeText={setNotes}
                editable={!isAttendanceAbsent}
                placeholder={isAttendanceAbsent ? 'הילד לא נכח בפגישה - הערות נעולות.' : 'הכנס הערות על הפגישה הנוכחית...'}
                placeholderTextColor="rgba(255,255,255,0.35)"
                multiline
                ref={notesInputRef}
                onFocus={() => {
                  setTimeout(() => {
                    scrollNotesInputAboveKeyboard();
                  }, 90);
                }}
              />
            )}
          </AppCard>
        </View>

        {/* Save Button (writable only) */}
        {mode === 'writable' && (
          <PrimaryButton
            label={isSaving ? 'שומרים...' : '💾 שמור דיווח פגישה'}
            style={styles.saveBtn}
            textStyle={styles.saveBtnText}
            gradientStyle={styles.saveBtnGradient}
            disabled={isSaving}
            onPress={() => saveReport()}
          />
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
  scrollContent: { 
    paddingTop: Platform.OS === 'ios' ? 120 : (StatusBar.currentHeight || 20) + 70, 
    paddingBottom: 60, 
    paddingHorizontal: 20 
  },
  waveContainer: { position: 'absolute', bottom: 0, left: (width - 2400) / 2, width: 2400, height: 150, zIndex: 0 },

  // Navbar
  navbar: {
    position: 'absolute', top: 0, left: 0, right: 0,
    paddingTop: Platform.OS === 'ios' ? 42 : (StatusBar.currentHeight || 20) + 6,
    paddingBottom: 8, paddingHorizontal: 16,
    flexDirection: 'row', justifyContent: 'flex-start', alignItems: 'center',
    zIndex: 100,
    borderBottomWidth: 1, borderBottomColor: 'rgba(255,255,255,0.1)',
  },
  backButton: {
    width: 42,
    height: 42,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.18)',
    backgroundColor: 'rgba(255,255,255,0.08)',
    justifyContent: 'center',
    alignItems: 'center',
  },
  backButtonIcon: {
    color: '#def6ff',
    fontSize: 22,
    textAlign: 'center',
  },

  // Page Header
  pageHeader: { alignItems: 'center', marginBottom: 44, zIndex: 10 },
  avatarGlow: {
    width: 84,
    height: 84,
    borderRadius: 42,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: 'rgba(0, 130, 176, 0.2)',
    borderWidth: 1,
    borderColor: 'rgba(188, 244, 255, 0.34)',
    shadowColor: '#00d4ff', shadowOffset: { width: 0, height: 0 },
    shadowOpacity: 0.6, shadowRadius: 20, elevation: 15, marginBottom: 12,
  },
  avatar: {
    width: 72, height: 72, borderRadius: 36,
    justifyContent: 'center', alignItems: 'center',
    overflow: 'hidden',
    borderWidth: 2, borderColor: 'rgba(255,255,255,0.6)',
  },
  avatarText: { color: '#001529', fontSize: 32, fontWeight: '900' },
  pageTitle: { color: '#fff', fontSize: 28, fontWeight: '800', writingDirection: 'rtl', marginBottom: 4 },
  childNameLabel: { color: '#00d4ff', fontSize: 16, fontWeight: '700', writingDirection: 'rtl' },

  // Toggle
  toggleRow: {
    flexDirection: 'row-reverse', justifyContent: 'center', gap: 12, marginBottom: 16, zIndex: 10,
  },
  toggleBtn: {
    paddingVertical: 10, paddingHorizontal: 20, borderRadius: 20,
    backgroundColor: 'rgba(255,255,255,0.08)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
  },
  toggleBtnActive: {
    backgroundColor: 'rgba(0, 212, 255, 0.2)',
    borderColor: '#00d4ff',
  },
  toggleText: { color: 'rgba(255,255,255,0.6)', fontSize: 14, fontWeight: '700', writingDirection: 'rtl' },
  toggleTextActive: { color: '#00d4ff' },

  // Info strip
  infoStrip: {
    backgroundColor: 'rgba(0, 212, 255, 0.1)',
    borderRadius: 14, paddingVertical: 8, paddingHorizontal: 14,
    marginBottom: 16, borderWidth: 1, borderColor: 'rgba(0, 212, 255, 0.2)',
    zIndex: 10,
  },
  infoText: { color: 'rgba(255,255,255,0.8)', textAlign: Platform.OS === 'web' ? 'right' : 'left', writingDirection: 'rtl', fontSize: 13, fontWeight: '600' },
  readableInfoMetaText: {
    marginTop: 6,
    color: 'rgba(255,255,255,0.65)',
    fontSize: 12,
  },
  readableInfoErrorText: {
    marginTop: 6,
    color: '#ffd3d3',
    fontSize: 12,
  },
  datePickerTrigger: {
    borderRadius: 8,
    paddingVertical: 2,
  },
  nativeDatePickerWrap: {
    marginTop: 10,
    borderRadius: 14,
    backgroundColor: 'rgba(255,255,255,0.96)',
    borderWidth: 1,
    borderColor: 'rgba(0, 212, 255, 0.35)',
    overflow: 'hidden',
    paddingBottom: 10,
  },
  iosDateActionsRow: {
    flexDirection: 'row-reverse',
    justifyContent: 'space-between',
    gap: 10,
    paddingHorizontal: 10,
    paddingTop: 2,
  },
  iosDateActionSecondary: {
    flex: 1,
    minHeight: 38,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: '#B7C8D8',
    backgroundColor: '#EEF4FA',
    alignItems: 'center',
    justifyContent: 'center',
  },
  iosDateActionSecondaryText: {
    color: '#3F5F7D',
    fontSize: 14,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
  iosDateActionPrimary: {
    flex: 1,
    minHeight: 38,
    borderRadius: 10,
    backgroundColor: '#00AEDD',
    alignItems: 'center',
    justifyContent: 'center',
  },
  iosDateActionPrimaryText: {
    color: '#FFFFFF',
    fontSize: 14,
    fontWeight: '800',
    writingDirection: 'rtl',
  },

  // Metrics Card
  attendanceCard: {
    padding: 20, borderRadius: 24, marginBottom: 16, zIndex: 10,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
  },
  attendanceToggleRow: {
    flexDirection: 'row-reverse',
    gap: 10,
    marginBottom: 10,
  },
  attendanceOption: {
    flex: 1,
    minHeight: 62,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.2)',
    backgroundColor: 'rgba(255,255,255,0.06)',
    justifyContent: 'center',
    alignItems: 'center',
  },
  attendanceOptionInner: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  attendanceEmoji: {
    fontSize: 22,
    marginBottom: 2,
  },
  attendanceOptionPresentActive: {
    borderColor: '#28c76f',
    backgroundColor: 'rgba(40, 199, 111, 0.22)',
  },
  attendanceOptionAbsentActive: {
    borderColor: '#ff6b6b',
    backgroundColor: 'rgba(255, 107, 107, 0.2)',
  },
  attendanceOptionText: {
    color: 'rgba(255,255,255,0.7)',
    fontSize: 14,
    fontWeight: '800',
    writingDirection: 'rtl',
  },
  attendanceOptionTextActive: {
    color: '#ffffff',
  },
  attendanceHintText: {
    color: 'rgba(255,255,255,0.72)',
    fontSize: 12,
    lineHeight: 18,
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
  },

  // Metrics Card
  metricsCard: {
    padding: 20, borderRadius: 24, marginBottom: 16, zIndex: 10,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
  },
  sectionTitle: { color: '#fff', fontSize: 20, fontWeight: '800', writingDirection: 'rtl', textAlign: Platform.OS === 'web' ? 'right' : 'left', marginBottom: 16 },
  exercisePickerTitle: {
    color: 'rgba(255,255,255,0.78)',
    fontSize: 13,
    fontWeight: '700',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginBottom: 10,
  },
  exercisePickerWrap: {
    flexDirection: 'row-reverse',
    flexWrap: 'wrap',
    gap: 10,
    marginBottom: 14,
  },
  exerciseChip: {
    minWidth: '48%',
    borderRadius: 14,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.16)',
    backgroundColor: 'rgba(255,255,255,0.05)',
    paddingVertical: 10,
    paddingHorizontal: 12,
  },
  exerciseChipActive: {
    borderColor: '#00d4ff',
    backgroundColor: 'rgba(0, 212, 255, 0.16)',
  },
  exerciseChipPercent: {
    color: '#d4e2ea',
    fontSize: 18,
    fontWeight: '900',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  exerciseChipPercentActive: {
    color: '#d6f7ff',
  },
  exerciseChipText: {
    color: 'rgba(255,255,255,0.78)',
    fontSize: 12,
    fontWeight: '700',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginTop: 4,
  },
  exerciseChipTextActive: {
    color: '#ffffff',
  },
  selectedExerciseBox: {
    borderRadius: 14,
    borderWidth: 1,
    borderColor: 'rgba(0, 212, 255, 0.2)',
    backgroundColor: 'rgba(0, 212, 255, 0.08)',
    padding: 14,
    marginBottom: 14,
  },
  selectedExerciseLabel: {
    color: 'rgba(255,255,255,0.65)',
    fontSize: 12,
    fontWeight: '700',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginBottom: 4,
  },
  selectedExerciseTitle: {
    color: '#ffffff',
    fontSize: 18,
    fontWeight: '900',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  selectedExerciseMeta: {
    color: 'rgba(255,255,255,0.72)',
    fontSize: 12,
    fontWeight: '700',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginTop: 6,
  },
  selectedExerciseHint: {
    marginTop: 8,
  },
  selectedExerciseHintText: {
    color: '#9fefff',
    fontSize: 12,
    fontWeight: '700',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  recommendationWrap: {
    backgroundColor: 'rgba(255,255,255,0.05)',
    borderRadius: 14,
    padding: 12,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.08)',
  },
  planHeaderRow: {
    flexDirection: 'row-reverse',
    justifyContent: 'space-between',
    marginBottom: 12,
  },
  planHeaderMeta: {
    color: 'rgba(255,255,255,0.72)',
    fontSize: 12,
    fontWeight: '700',
  },
  nextExerciseBox: {
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(0, 212, 255, 0.35)',
    backgroundColor: 'rgba(0, 212, 255, 0.12)',
    padding: 12,
    marginBottom: 12,
  },
  nextExerciseTitle: {
    color: 'rgba(255,255,255,0.78)',
    fontSize: 12,
    fontWeight: '700',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginBottom: 4,
  },
  nextExerciseName: {
    color: '#d6f7ff',
    fontSize: 18,
    fontWeight: '900',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginBottom: 6,
  },
  nextExerciseReason: {
    color: 'rgba(255,255,255,0.75)',
    fontSize: 13,
    lineHeight: 20,
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  planListTitle: {
    color: '#fff',
    fontSize: 14,
    fontWeight: '800',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginBottom: 8,
  },
  planRow: {
    flexDirection: 'row-reverse',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingVertical: 8,
    borderBottomWidth: 1,
    borderBottomColor: 'rgba(255,255,255,0.08)',
  },
  planRowText: {
    color: 'rgba(255,255,255,0.85)',
    fontSize: 13,
    fontWeight: '700',
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    flex: 1,
  },
  planRowMeta: {
    color: 'rgba(255,255,255,0.6)',
    fontSize: 12,
    marginLeft: 8,
  },

  // Metric rows
  metricRow: { marginBottom: 18 },
  metricLabelRow: { flexDirection: 'row-reverse', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 },
  metricLabelActionsRow: { flexDirection: 'row-reverse', alignItems: 'center', justifyContent: 'flex-end', flexShrink: 1, marginLeft: 12 },
  metricLabel: { color: 'rgba(255,255,255,0.85)', fontSize: 14, fontWeight: '600', writingDirection: 'rtl', textAlign: Platform.OS === 'web' ? 'right' : 'left', flexShrink: 1 },
  metricLabelDisabled: { color: 'rgba(255,255,255,0.45)' },
  metricPercent: { color: 'rgba(255,255,255,0.6)', fontSize: 13, fontWeight: '800' },
  metricPercentDisabled: { color: 'rgba(255,255,255,0.45)' },
  metricRemoveButton: {
    minWidth: 32,
    height: 24,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.25)',
    backgroundColor: 'rgba(255,255,255,0.08)',
    alignItems: 'center',
    justifyContent: 'center',
    marginRight: 8,
  },
  metricRemoveButtonDisabled: {
    borderColor: 'rgba(255,255,255,0.12)',
    backgroundColor: 'rgba(255,255,255,0.05)',
  },
  metricRemoveButtonText: {
    color: '#ffdede',
    fontSize: 14,
    fontWeight: '800',
  },
  metricRemoveButtonTextDisabled: {
    color: 'rgba(255,255,255,0.5)',
  },
  noMetricsText: {
    color: 'rgba(255,255,255,0.72)',
    fontSize: 13,
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginBottom: 10,
  },
  removedMetricsWrap: {
    marginTop: 4,
  },
  removedMetricsTitle: {
    color: 'rgba(255,255,255,0.64)',
    fontSize: 12,
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
    marginBottom: 8,
  },
  removedMetricsList: {
    flexDirection: 'row-reverse',
    flexWrap: 'wrap',
  },
  restoreMetricChip: {
    borderRadius: 999,
    borderWidth: 1,
    borderColor: 'rgba(0,212,255,0.4)',
    backgroundColor: 'rgba(0, 212, 255, 0.14)',
    paddingVertical: 6,
    paddingHorizontal: 10,
    marginLeft: 8,
    marginBottom: 8,
  },
  restoreMetricChipDisabled: {
    borderColor: 'rgba(255,255,255,0.2)',
    backgroundColor: 'rgba(255,255,255,0.08)',
  },
  restoreMetricChipText: {
    color: '#d6f7ff',
    fontSize: 12,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
  restoreMetricChipTextDisabled: {
    color: 'rgba(255,255,255,0.55)',
  },

  // Read-only track
  trackOuter: { height: 10, borderRadius: 6, backgroundColor: 'rgba(255,255,255,0.1)', overflow: 'hidden', direction: 'ltr' },
  trackFill: { height: '100%', borderRadius: 6 },

  // Writable track
  trackTouchZone: { height: 36, justifyContent: 'center', marginHorizontal: 14, direction: 'ltr' },
  trackOuterWritable: { height: 10, borderRadius: 6, backgroundColor: 'rgba(255,255,255,0.1)', overflow: 'hidden', marginHorizontal: -14, direction: 'ltr' },
  trackOuterWritableDisabled: {
    backgroundColor: 'rgba(255,255,255,0.06)',
  },
  trackFillDisabled: {
    opacity: 0.75,
  },
  thumbOuter: {
    position: 'absolute', top: 4, width: 28, height: 28, borderRadius: 14, marginLeft: -14,
    backgroundColor: 'rgba(0, 212, 255, 0.3)',
    justifyContent: 'center', alignItems: 'center',
    shadowColor: '#00d4ff', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 0.6, shadowRadius: 8, elevation: 6,
  },
  thumbOuterDisabled: {
    backgroundColor: 'rgba(125, 135, 152, 0.24)',
    shadowColor: '#7d8798',
    shadowOpacity: 0.35,
  },
  thumbInner: { width: 18, height: 18, borderRadius: 9 },

  // Notes Card
  notesCard: {
    padding: 20, borderRadius: 24, marginBottom: 16, zIndex: 10,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
  },
  notesReadBox: {
    backgroundColor: 'rgba(255,255,255,0.05)', borderRadius: 14, padding: 14, minHeight: 80,
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.08)',
  },
  notesReadMetaText: {
    color: 'rgba(255,255,255,0.72)',
    fontSize: 13,
    lineHeight: 21,
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  notesReadText: { color: '#ffffff', fontSize: 16, lineHeight: 28, textAlign: Platform.OS === 'web' ? 'right' : 'left', writingDirection: 'rtl' },
  notesReadErrorText: {
    color: '#ffd3d3',
  },
  notesReadSourceText: {
    marginTop: 10,
    color: 'rgba(255,255,255,0.5)',
    fontSize: 12,
    textAlign: Platform.OS === 'web' ? 'right' : 'left',
    writingDirection: 'rtl',
  },
  notesInput: {
    backgroundColor: 'rgba(255,255,255,0.05)', borderRadius: 14, padding: 14, minHeight: 100,
    borderWidth: 1, borderColor: 'rgba(0, 212, 255, 0.25)',
    color: '#fff', fontSize: 14, textAlign: 'right', writingDirection: 'rtl', textAlignVertical: 'top',
  },
  notesInputDisabled: {
    borderColor: 'rgba(128,138,152,0.5)',
    backgroundColor: 'rgba(120,130,144,0.22)',
    color: 'rgba(255,255,255,0.6)',
  },
  disabledCard: {
    opacity: 0.72,
    borderColor: 'rgba(130, 140, 156, 0.65)',
  },

  // Save Button
  saveBtn: { borderRadius: 20, marginBottom: 12, zIndex: 10, ...shadows.glowPrimary },
  saveBtnText: { color: '#001529', fontSize: 17, fontWeight: '800', writingDirection: 'rtl' },
  saveBtnGradient: { paddingVertical: 16, alignItems: 'center', justifyContent: 'center' },

  // Lesson Dates Dropdown
  lessonDatesOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.55)',
    justifyContent: 'center',
    alignItems: 'center',
  },
  lessonDatesModal: {
    width: '85%',
    maxHeight: '70%',
    backgroundColor: '#ffffff',
    borderRadius: 16,
    padding: 20,
    elevation: 8,
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.25,
    shadowRadius: 12,
  },
  lessonDatesTitle: {
    fontSize: 17,
    fontWeight: '700',
    color: '#001529',
    textAlign: 'center',
    marginBottom: 14,
    writingDirection: 'rtl',
  },
  lessonDatesList: {
    maxHeight: 300,
  },
  lessonDateItem: {
    paddingVertical: 12,
    paddingHorizontal: 16,
    borderRadius: 10,
    marginBottom: 6,
    backgroundColor: '#f0f7fc',
    borderWidth: 1,
    borderColor: '#e0eaf3',
  },
  lessonDateItemSelected: {
    backgroundColor: '#d4f4ff',
    borderColor: '#00AEDD',
  },
  lessonDateText: {
    fontSize: 15,
    color: '#1a3a52',
    textAlign: 'center',
    fontWeight: '500',
  },
  lessonDateTextSelected: {
    color: '#006890',
    fontWeight: '700',
  },
  lessonDatesCloseBtn: {
    marginTop: 12,
    paddingVertical: 10,
    borderRadius: 10,
    backgroundColor: '#EEF4FA',
    borderWidth: 1,
    borderColor: '#B7C8D8',
    alignItems: 'center',
  },
  lessonDatesCloseBtnText: {
    fontSize: 14,
    fontWeight: '700',
    color: '#3F5F7D',
  },
});

