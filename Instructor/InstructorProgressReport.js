import React, { useRef, useEffect, useState, useMemo } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  StatusBar, Easing, Platform, TextInput, Alert, findNodeHandle,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import DateTimePicker, { DateTimePickerAndroid } from '@react-native-community/datetimepicker';
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
// 2. REINCARNATING BUBBLES
// ========================================
const Bubble = () => {
  const progress = useRef(new Animated.Value(0)).current;
  const sway = useRef(new Animated.Value(0)).current;
  const [config, setConfig] = useState({ size: Math.random() * 30 + 10, left: Math.random() * width });
  const durationRef = useRef(Math.random() * 16000 + 14000);
  const isFirstRun = useRef(true);

  const startFloat = () => {
    progress.setValue(0);
    const delay = isFirstRun.current ? Math.random() * 30000 : 0;
    isFirstRun.current = false;
    Animated.sequence([
      Animated.delay(delay),
      Animated.timing(progress, { toValue: 1, duration: durationRef.current, easing: Easing.linear, useNativeDriver: true }),
    ]).start(({ finished }) => {
      if (finished) {
        durationRef.current = Math.random() * 16000 + 14000;
        setConfig({ size: Math.random() * 30 + 10, left: Math.random() * width });
        startFloat();
      }
    });
  };

  useEffect(() => {
    startFloat();
    const swayAnim = Animated.loop(
      Animated.sequence([
        Animated.timing(sway, { toValue: 1, duration: 2000, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
        Animated.timing(sway, { toValue: -1, duration: 4000, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
        Animated.timing(sway, { toValue: 0, duration: 2000, easing: Easing.inOut(Easing.sin), useNativeDriver: true }),
      ]),
    );
    swayAnim.start();
    return () => { progress.stopAnimation(); sway.stopAnimation(); };
  }, []);

  const translateY = progress.interpolate({ inputRange: [0, 1], outputRange: [height + 50, -150] });
  const opacity = progress.interpolate({ inputRange: [0, 0.1, 0.8, 1], outputRange: [0, 1, 1, 0] });
  const translateX = sway.interpolate({ inputRange: [-1, 1], outputRange: [-20, 20] });

  return (
    <Animated.View style={[styles.bubbleWrapper, { width: config.size, height: config.size, borderRadius: config.size / 2, left: config.left, transform: [{ translateY }, { translateX }], opacity }]}>
      <LinearGradient colors={["rgba(255,255,255,0.95)", "rgba(255,255,255,0.1)"]} style={{ flex: 1, borderRadius: config.size / 2, borderWidth: 1, borderColor: 'rgba(255,255,255,0.4)' }} />
    </Animated.View>
  );
};

// ========================================
// 3. PROGRESS BARS
// ========================================
const initialRows = [
  { label: 'הסתגלות וביטחון במים', value: 9 },
  { label: 'שליטה בנשימות (הכנסת ראש למים)', value: 8 },
  { label: 'תנועתיות וקואורדינציה', value: 7 },
  { label: 'יציבה וציפה', value: 8 },
  { label: 'תקשורת במים (ושיתוף פעולה)', value: 9 },
  { label: 'התמדה ומאמץ', value: 10 },
];

const RATING_MIN = 0;
const RATING_MAX = 10;

const createEmptyMetricRows = () => {
  return initialRows.map((metric) => ({
    ...metric,
    value: RATING_MIN,
  }));
};

const createWritableMetricRows = () => {
  return initialRows.map((metric) => ({
    ...metric,
    isIncluded: true,
  }));
};

const normalizeProgressValue = (rawValue) => {
  if (!Number.isFinite(rawValue)) return RATING_MIN;
  const clamped = Math.max(RATING_MIN, Math.min(RATING_MAX, rawValue));
  return Math.round(clamped);
};

const formatRatingValue = (rawValue) => {
  return `${normalizeProgressValue(rawValue)}`;
};

const calcMedian = (values) => {
  const sortedValues = values
    .filter((value) => Number.isFinite(value))
    .slice()
    .sort((a, b) => a - b);

  if (sortedValues.length === 0) return RATING_MIN;

  const middleIndex = Math.floor(sortedValues.length / 2);
  if (sortedValues.length % 2 === 1) {
    return sortedValues[middleIndex];
  }

  return (sortedValues[middleIndex - 1] + sortedValues[middleIndex]) / 2;
};

const buildMedianMetrics = (reports, metricsTemplate) => {
  const metricsByLabel = new Map(
    metricsTemplate.map((metric) => [String(metric.label || '').trim(), []]),
  );

  (Array.isArray(reports) ? reports : []).forEach((report) => {
    const rawNotes = report?.notes;

    let parsedNotes = null;
    if (rawNotes && typeof rawNotes === 'object') {
      parsedNotes = rawNotes;
    } else if (typeof rawNotes === 'string' && rawNotes.trim()) {
      try {
        parsedNotes = JSON.parse(rawNotes);
      } catch (_error) {
        parsedNotes = null;
      }
    }

    const metricsList = Array.isArray(parsedNotes?.metrics) ? parsedNotes.metrics : [];

    metricsList.forEach((metric, index) => {
      const rawValue = Number(metric?.value);
      if (!Number.isFinite(rawValue)) {
        return;
      }

      const normalizedValue = normalizeProgressValue(rawValue);
      const metricLabel = String(metric?.label || '').trim();

      if (metricsByLabel.has(metricLabel)) {
        metricsByLabel.get(metricLabel).push(normalizedValue);
        return;
      }

      // Fallback by index for malformed/garbled labels in historical notes JSON.
      if (index >= 0 && index < metricsTemplate.length) {
        const fallbackLabel = String(metricsTemplate[index].label || '').trim();
        metricsByLabel.get(fallbackLabel).push(normalizedValue);
      }
    });
  });

  return metricsTemplate.map((metric) => {
    const label = String(metric.label || '').trim();
    const values = metricsByLabel.get(label) || [];

    if (values.length === 0) {
      return {
        ...metric,
        value: RATING_MIN,
      };
    }

    return {
      ...metric,
      value: normalizeProgressValue(calcMedian(values)),
    };
  });
};

const formatReportDate = (dateValue) => {
  if (!(dateValue instanceof Date) || Number.isNaN(dateValue.getTime())) {
    return '';
  }

  return dateValue.toLocaleDateString('he-IL', {
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
    return 'שירות ה-AI לא זמין כרגע או הגיע למגבלת שימוש זמנית. אפשר לרענן ולנסות שוב בעוד כמה דקות.';
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
  const safeVal = normalizeProgressValue(value || 0);
  const fillPercentage = (safeVal / RATING_MAX) * 100;

  return (
    <View style={styles.metricRow}>
      <View style={styles.metricLabelRow}>
        <Text style={styles.metricPercent}>{formatRatingValue(safeVal)}/10</Text>
        <Text style={styles.metricLabel}>{label}</Text>
      </View>
      <View style={styles.trackOuter}>
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
    const nextValue = normalizeProgressValue((touchX / measuredWidth) * RATING_MAX);
    onChange(nextValue);
  };

  const safeVal = normalizeProgressValue(value);
  const fillPercentage = (safeVal / RATING_MAX) * 100;

  return (
    <View style={styles.metricRow}>
      <View style={styles.metricLabelRow}>
        <Text style={[styles.metricPercent, disabled ? styles.metricPercentDisabled : { color: '#00d4ff' }]}>{formatRatingValue(safeVal)}/10</Text>
        <View style={styles.metricLabelActionsRow}>
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
          <Text style={[styles.metricLabel, disabled && styles.metricLabelDisabled]}>{label}</Text>
        </View>
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
      >
        <View style={[styles.trackOuterWritable, disabled && styles.trackOuterWritableDisabled]} pointerEvents="none">
          <LinearGradient
            colors={disabled ? ['#7d8798', '#677182'] : ['#00d4ff', '#0099cc']}
            start={{ x: 0, y: 0 }} end={{ x: 1, y: 0 }}
            style={[styles.trackFill, disabled && styles.trackFillDisabled, { width: `${fillPercentage}%` }]}
          />
        </View>
        {/* Glowing Thumb */}
        <View pointerEvents="none" style={[styles.thumbOuter, disabled && styles.thumbOuterDisabled, { left: `${fillPercentage}%` }]}>
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
  const [metrics, setMetrics] = useState(() => createWritableMetricRows());
  const [readableMetrics, setReadableMetrics] = useState(createEmptyMetricRows());
  const [isLoadingReadableMetrics, setIsLoadingReadableMetrics] = useState(false);
  const [readableMetricsError, setReadableMetricsError] = useState('');
  const [readableAiSummary, setReadableAiSummary] = useState('');
  const [readableAiSummarySource, setReadableAiSummarySource] = useState('');
  const [isLoadingReadableSummary, setIsLoadingReadableSummary] = useState(false);
  const [readableSummaryError, setReadableSummaryError] = useState('');
  const [notes, setNotes] = useState('');
  const [isPresent, setIsPresent] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isMetricInteractionActive, setIsMetricInteractionActive] = useState(false);
  const [reportDate, setReportDate] = useState(new Date());
  const [showIosNativeDatePicker, setShowIosNativeDatePicker] = useState(false);
  const [draftIosReportDate, setDraftIosReportDate] = useState(new Date());
  const [notesCardY, setNotesCardY] = useState(0);
  const scrollViewRef = useRef(null);
  const notesInputRef = useRef(null);

  const child = route?.params?.child || {};
  const authUser = route?.params?.authUser || {};
  const childId = resolveNumericId(route?.params?.childId, child?.childId, child?.id);
  const instructorId = resolveNumericId(route?.params?.instructorId, authUser?.id, child?.instructorId);
  const groupId = resolveNumericId(route?.params?.groupId, child?.groupId);
  const childName = child.name || 'ילד לא ידוע';
  const initial = childName[0] || 'א';
  const isAttendanceAbsent = mode === 'writable' && !isPresent;

  const bubblePool = useMemo(() => Array.from({ length: 6 }).map((_, i) => i), []);

  const surfaceOpacity = scrollY.interpolate({ inputRange: [0, height * 0.8], outputRange: [1, 0], extrapolate: 'clamp' });
  const navBg = scrollY.interpolate({ inputRange: [0, 50], outputRange: ['rgba(0, 21, 41, 0.4)', 'rgba(0, 21, 41, 0.85)'], extrapolate: 'clamp' });

  const updateMetric = (idx, newValue) => {
    setMetrics((prevMetrics) => {
      const nextMetrics = [...prevMetrics];
      if (!nextMetrics[idx] || nextMetrics[idx].isIncluded === false) {
        return prevMetrics;
      }

      nextMetrics[idx] = {
        ...nextMetrics[idx],
        value: normalizeProgressValue(newValue),
      };

      return nextMetrics;
    });
  };

  const setMetricIncluded = (idx, isIncluded) => {
    setMetrics((prevMetrics) => {
      const nextMetrics = [...prevMetrics];
      if (!nextMetrics[idx]) {
        return prevMetrics;
      }

      nextMetrics[idx] = {
        ...nextMetrics[idx],
        isIncluded,
      };

      return nextMetrics;
    });
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

  useEffect(() => {
    if (mode !== 'writable' && showIosNativeDatePicker) {
      setShowIosNativeDatePicker(false);
    }
  }, [mode, showIosNativeDatePicker]);

  useEffect(() => {
    if (!showIosNativeDatePicker) {
      setDraftIosReportDate(reportDate);
    }
  }, [showIosNativeDatePicker, reportDate]);

  useEffect(() => {
    if (mode !== 'readable') {
      return;
    }

    if (!childId || !instructorId) {
      setReadableMetrics(createEmptyMetricRows());
      setReadableMetricsError('לא ניתן להציג מדדים כלליים ללא מזהה ילד ומדריך.');
      setIsLoadingReadableMetrics(false);
      return;
    }

    let isCancelled = false;

    const loadReadableMetrics = async () => {
      try {
        setIsLoadingReadableMetrics(true);
        setReadableMetricsError('');

        const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children/${childId}/reports`);
        const payload = await response.json().catch(() => null);

        if (!response.ok) {
          throw new Error(payload?.message || 'לא ניתן לטעון את היסטוריית הדיווחים של הילד.');
        }

        const reports = Array.isArray(payload) ? payload : [];
        const nextReadableMetrics = buildMedianMetrics(reports, initialRows);

        if (!isCancelled) {
          setReadableMetrics(nextReadableMetrics);
        }
      } catch (error) {
        if (!isCancelled) {
          setReadableMetrics(createEmptyMetricRows());
          setReadableMetricsError(error?.message || 'לא ניתן לחשב מדדים כלליים כרגע.');
        }
      } finally {
        if (!isCancelled) {
          setIsLoadingReadableMetrics(false);
        }
      }
    };

    loadReadableMetrics();

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

        const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children/${childId}/reports/summary`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
        });

        const payload = await response.json().catch(() => null);

        if (!response.ok) {
          throw new Error(resolveSummaryErrorMessage(payload));
        }

        const summaryText = String(payload?.summary || '').trim();
        if (!summaryText) {
          throw new Error('התקבלה תשובה ריקה מהשרת, נסו לרענן שוב בעוד רגע.');
        }

        if (!isCancelled) {
          setReadableAiSummary(summaryText);
          setReadableAiSummarySource(String(payload?.source || '').trim());
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
    if (Platform.OS === 'android') {
      DateTimePickerAndroid.open({
        value: reportDate,
        mode: 'date',
        display: 'calendar',
        minimumDate: new Date(2000, 0, 1),
        maximumDate: new Date(2100, 11, 31),
        onChange: (event, selectedDate) => {
          if (event.type === 'set' && selectedDate) {
            setReportDate(selectedDate);
          }
        },
      });
      return;
    }

    if (Platform.OS === 'web') {
      if (typeof window === 'undefined' || typeof window.prompt !== 'function') {
        return;
      }

      const defaultDate = toIsoDateString(reportDate);
      const userInput = window.prompt('הזינו תאריך בפורמט YYYY-MM-DD או DD/MM/YYYY', defaultDate);
      if (userInput == null) {
        return;
      }

      const parsedDate = parseReportDateInput(userInput);
      if (!parsedDate) {
        Alert.alert('תאריך לא תקין', 'אנא הזינו תאריך בפורמט תקין, לדוגמה 2026-04-05 או 05/04/2026.');
        return;
      }

      setReportDate(parsedDate);
      return;
    }

    setDraftIosReportDate(reportDate);
    setShowIosNativeDatePicker((prev) => !prev);
  };

  const cancelIosDateSelection = () => {
    setDraftIosReportDate(reportDate);
    setShowIosNativeDatePicker(false);
  };

  const confirmIosDateSelection = () => {
    setReportDate(draftIosReportDate);
    setShowIosNativeDatePicker(false);
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

  const saveReport = async () => {
    if (isSaving) {
      return;
    }

    if (!childId || !instructorId) {
      Alert.alert('חסרים פרטים', 'לא ניתן לשמור דיווח בלי מזהה ילד ומזהה מדריך תקינים. חזרו לפרופיל הילד ונסו שוב.');
      return;
    }

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

    const submitReport = async (overwriteExisting) => {
      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/children/${childId}/reports`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          groupId: groupId || null,
          reportDate: toIsoDateString(reportDate),
          isPresent: isPresentInMeeting,
          overwriteExisting,
          comment: normalizedComment,
          metrics: normalizedMetrics,
        }),
      });

      const payload = await response.json().catch(() => null);
      return { response, payload };
    };

    const overwriteExistingReport = async () => {
      try {
        setIsSaving(true);

        const { response, payload } = await submitReport(true);
        if (!response.ok) {
          throw new Error(payload?.message || 'עדכון הדיווח הקיים נכשל. נסו שוב.');
        }

        Alert.alert('מעולה', 'הדיווח הקיים עודכן בהצלחה!');
        navigation.goBack();
      } catch (error) {
        Alert.alert('שגיאת שמירה', error?.message || 'לא ניתן לעדכן כרגע את הדיווח הקיים. נסו שוב מאוחר יותר.');
      } finally {
        setIsSaving(false);
      }
    };

    try {
      setIsSaving(true);

      const { response, payload } = await submitReport(false);

      if (response.status === 409 && String(payload?.code || '').trim() === 'report_exists_for_date') {
        setIsSaving(false);

        Alert.alert(
          'כבר קיים דיווח בתאריך הזה',
          'כבר קיים דיווח לילד בתאריך שבחרת. האם תרצה לערוך את הדיווח הקיים או להשאיר אותו כמו שהוא?',
          [
            {
              text: 'להשאיר כמו שהוא',
              style: 'cancel',
            },
            {
              text: 'ערוך את הדיווח הקיים',
              onPress: () => {
                void overwriteExistingReport();
              },
            },
          ],
          { cancelable: true },
        );

        return;
      }

      if (!response.ok) {
        throw new Error(payload?.message || 'שמירת הדיווח נכשלה. נסו שוב.');
      }

      Alert.alert('מעולה', 'הדיווח נשמר בהצלחה!');
      navigation.goBack();
    } catch (error) {
      Alert.alert('שגיאת שמירה', error?.message || 'לא ניתן לשמור כרגע את הדיווח. נסו שוב מאוחר יותר.');
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
        <LinearGradient colors={['#00aed8', '#007fa7', '#004d73']} style={StyleSheet.absoluteFill} />
      </Animated.View>

      {bubblePool.map((id) => <Bubble key={id} />)}

      <Wave color="rgba(0, 153, 204, 0.3)" duration={8000} startPosition={0} d="M0,75 C200,45 400,105 600,75 C800,45 1000,105 1200,75 L1200,150 L0,150 Z M1200,75 C1400,45 1600,105 1800,75 C2000,45 2200,105 2400,75 L2400,150 L1200,150 Z" />
      <Wave color="rgba(0, 212, 255, 0.25)" duration={6000} startPosition={-600} d="M0,100 C300,60 500,130 800,90 C1000,60 1100,120 1200,100 L1200,150 L0,150 Z M1200,100 C1500,60 1700,130 2000,90 C2200,60 2300,120 2400,100 L2400,150 L1200,150 Z" />

      {/* NAVBAR */}
      <AnimatedBlurView intensity={20} tint="dark" style={[styles.navbar, { backgroundColor: navBg }]}>
        <TouchableOpacity onPress={() => navigation.goBack()} style={styles.backButton}>
          <Text style={styles.backButtonIcon}>{'\u2039'}</Text>
        </TouchableOpacity>
      </AnimatedBlurView>

      {/* SCROLLABLE CONTENT */}
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
            <Text style={[styles.toggleText, mode === 'readable' && styles.toggleTextActive]}>📊 מדדים כלליים</Text>
          </TouchableOpacity>
        </View>

        {/* Info Strip */}
        <View style={styles.infoStrip}>
          {mode === 'readable' ? (
            <View>
              <Text style={styles.infoText}>📅 תאריכים: מתחילת השנה ועד היום</Text>
              {isLoadingReadableMetrics ? (
                <Text style={[styles.infoText, styles.readableInfoMetaText]}>מחשב חציון מדדים מכל הדיווחים...</Text>
              ) : readableMetricsError ? (
                <Text style={[styles.infoText, styles.readableInfoErrorText]}>{readableMetricsError}</Text>
              ) : null}
            </View>
          ) : (
            <View>
              <TouchableOpacity activeOpacity={0.8} onPress={openReportDatePicker} style={styles.datePickerTrigger}>
                <Text style={styles.infoText}>📅 תאריך דיווח: {formatReportDate(reportDate)} (לחץ לשינוי)</Text>
              </TouchableOpacity>

              {Platform.OS === 'ios' && showIosNativeDatePicker ? (
                <View style={styles.nativeDatePickerWrap}>
                  <DateTimePicker
                    value={draftIosReportDate}
                    mode="date"
                    display={Number(Platform.Version) >= 14 ? 'inline' : 'spinner'}
                    themeVariant="light"
                    minimumDate={new Date(2000, 0, 1)}
                    maximumDate={new Date(2100, 11, 31)}
                    onChange={(_event, selectedDate) => {
                      if (selectedDate) {
                        setDraftIosReportDate(selectedDate);
                      }
                    }}
                  />

                  <View style={styles.iosDateActionsRow}>
                    <TouchableOpacity style={styles.iosDateActionSecondary} activeOpacity={0.85} onPress={cancelIosDateSelection}>
                      <Text style={styles.iosDateActionSecondaryText}>ביטול</Text>
                    </TouchableOpacity>
                    <TouchableOpacity style={styles.iosDateActionPrimary} activeOpacity={0.85} onPress={confirmIosDateSelection}>
                      <Text style={styles.iosDateActionPrimaryText}>אישור</Text>
                    </TouchableOpacity>
                  </View>
                </View>
              ) : null}
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
          <Text style={styles.sectionTitle}>מדדי התקדמות</Text>
          {mode === 'readable' ? (
            readableMetrics.map((item) => <ReadOnlyBar key={item.label} label={item.label} value={item.value} />)
          ) : (
            <View>
              {metrics.every((item) => item?.isIncluded === false) ? (
                <Text style={styles.noMetricsText}>לא נבחרו מדדים לדיווח הנוכחי.</Text>
              ) : null}

              {metrics.map((item, idx) => {
                if (item?.isIncluded === false) {
                  return null;
                }

                return (
                  <WritableBar
                    key={item.label}
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
                  <Text style={styles.removedMetricsTitle}>מדדים שהוסרו מהדיווח הזה (לחץ להחזרה):</Text>
                  <View style={styles.removedMetricsList}>
                    {metrics.map((item, idx) => {
                      if (item?.isIncluded !== false) {
                        return null;
                      }

                      return (
                        <TouchableOpacity
                          key={`restore-${item.label}`}
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
          <Text style={styles.sectionTitle}>הערות של הבינה המלאכותית</Text>
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
            onPress={saveReport}
          />
        )}
      </Animated.ScrollView>
    </View>
  );
}

// ========================================
// STYLES
// ========================================
const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep },
  scrollContent: { paddingTop: 110, paddingBottom: 60, paddingHorizontal: 20 },
  waveContainer: { position: 'absolute', bottom: 0, left: 0, width: 2400, height: 150, zIndex: 0 },
  bubbleWrapper: { position: 'absolute', zIndex: 1 },

  // Navbar
  navbar: {
    position: 'absolute', top: 0, left: 0, right: 0,
    paddingTop: Platform.OS === 'ios' ? 50 : (StatusBar.currentHeight || 20) + 15,
    paddingBottom: 15, paddingHorizontal: 20,
    flexDirection: 'row', justifyContent: 'flex-start', alignItems: 'center',
    zIndex: 100,
    borderBottomWidth: 1, borderBottomColor: 'rgba(255,255,255,0.1)',
  },
  backButton: {
    width: 52,
    height: 52,
    borderRadius: 26,
    backgroundColor: 'rgba(255,255,255,0.14)',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.24)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  backButtonIcon: {
    color: '#FFFFFF',
    fontSize: 34,
    fontWeight: '700',
    lineHeight: 34,
  },

  // Page Header
  pageHeader: { alignItems: 'center', marginBottom: 24, zIndex: 10 },
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
  infoText: { color: 'rgba(255,255,255,0.8)', textAlign: 'right', writingDirection: 'rtl', fontSize: 13, fontWeight: '600' },
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
    flexDirection: 'row',
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
    textAlign: 'right',
    writingDirection: 'rtl',
  },

  // Metrics Card
  metricsCard: {
    padding: 20, borderRadius: 24, marginBottom: 16, zIndex: 10,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderWidth: 1, borderColor: 'rgba(255,255,255,0.15)',
  },
  sectionTitle: { color: '#fff', fontSize: 20, fontWeight: '800', writingDirection: 'rtl', textAlign: 'right', marginBottom: 16 },

  // Metric rows
  metricRow: { marginBottom: 18 },
  metricLabelRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 },
  metricLabelActionsRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'flex-end', flexShrink: 1, marginLeft: 12 },
  metricLabel: { color: 'rgba(255,255,255,0.85)', fontSize: 14, fontWeight: '600', writingDirection: 'rtl', textAlign: 'right', flexShrink: 1 },
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
    textAlign: 'right',
    writingDirection: 'rtl',
    marginBottom: 10,
  },
  removedMetricsWrap: {
    marginTop: 4,
  },
  removedMetricsTitle: {
    color: 'rgba(255,255,255,0.64)',
    fontSize: 12,
    textAlign: 'right',
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
  trackOuter: { height: 10, borderRadius: 6, backgroundColor: 'rgba(255,255,255,0.1)', overflow: 'hidden' },
  trackFill: { height: '100%', borderRadius: 6 },

  // Writable track
  trackTouchZone: { height: 36, justifyContent: 'center' },
  trackOuterWritable: { height: 10, borderRadius: 6, backgroundColor: 'rgba(255,255,255,0.1)', overflow: 'hidden' },
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
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  notesReadText: { color: 'rgba(255,255,255,0.7)', fontSize: 14, lineHeight: 22, textAlign: 'right', writingDirection: 'rtl' },
  notesReadErrorText: {
    color: '#ffd3d3',
  },
  notesReadSourceText: {
    marginTop: 10,
    color: 'rgba(255,255,255,0.5)',
    fontSize: 12,
    textAlign: 'right',
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
});

