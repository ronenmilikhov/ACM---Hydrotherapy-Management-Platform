import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Animated,
  Dimensions,
  ScrollView,
  StatusBar,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
  ActivityIndicator,
  Alert,
  Easing,
  Platform,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import * as Print from 'expo-print';
import * as Sharing from 'expo-sharing';
import styles from './ParentProgressReport.styles';
import { useFocusEffect } from '@react-navigation/native';
import PrimaryButton from '../components/ui/PrimaryButton';
import { colors } from '../theme/tokens';
import { API_BASE_URL } from '../apiConfig';
import {
  countUnreadNotifications,
  fetchParentNotifications,
} from '../notifications/parentNotifications';

const { width, height } = Dimensions.get('window');

const initialRows = [
  { label: 'הסתגלות וביטחון במים', value: 0 },
  { label: 'שליטה בנשימות (הכנסת ראש למים)', value: 0 },
  { label: 'תנועתיות וקואורדינציה', value: 0 },
  { label: 'יציבה וציפה', value: 0 },
  { label: 'תקשורת במים (ושיתוף פעולה)', value: 0 },
  { label: 'התמדה ומאמץ', value: 0 },
];

const RATING_MIN = 0;
const RATING_MAX = 5;

const normalizeProgressValue = (rawValue) => {
  if (!Number.isFinite(rawValue)) return RATING_MIN;
  const clamped = Math.max(RATING_MIN, Math.min(RATING_MAX, rawValue));
  return Math.round(clamped);
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

const parseNotesObject = (rawNotes) => {
  if (rawNotes && typeof rawNotes === 'object') {
    return rawNotes;
  }

  if (typeof rawNotes === 'string' && rawNotes.trim()) {
    try {
      return JSON.parse(rawNotes);
    } catch (_error) {
      return null;
    }
  }

  return null;
};

const buildMedianMetrics = (reports, metricsTemplate) => {
  const metricsByLabel = new Map(
    metricsTemplate.map((metric) => [String(metric.label || '').trim(), []]),
  );

  (Array.isArray(reports) ? reports : []).forEach((report) => {
    let metricsList = [];
    if (Array.isArray(report?.metrics)) {
      metricsList = report.metrics;
    } else if (typeof report?.metrics === 'string' && report.metrics.trim()) {
      try {
        metricsList = JSON.parse(report.metrics);
      } catch (e) { }
    }

    if (!Array.isArray(metricsList) || metricsList.length === 0) {
      const parsedNotes = parseNotesObject(report?.notes || report?.Notes);
      metricsList = Array.isArray(parsedNotes?.metrics) ? parsedNotes.metrics : [];
    }

    metricsList.forEach((metric, index) => {
      if (metric?.isIncluded === false) return;

      const rawValue = Number(metric?.value);
      if (!Number.isFinite(rawValue)) return;

      const normalizedValue = normalizeProgressValue(rawValue);
      const metricLabel = String(metric?.label || '').trim();

      if (metricsByLabel.has(metricLabel)) {
        metricsByLabel.get(metricLabel).push(normalizedValue);
        return;
      }

      if (index >= 0 && index < metricsTemplate.length) {
        const fallbackLabel = String(metricsTemplate[index].label || '').trim();
        metricsByLabel.get(fallbackLabel).push(normalizedValue);
      }
    });
  });

  return metricsTemplate.map((metric) => {
    const label = String(metric.label || '').trim();
    const values = metricsByLabel.get(label) || [];
    if (values.length === 0) return { ...metric, value: RATING_MIN };
    return { ...metric, value: normalizeProgressValue(calcMedian(values)) };
  });
};

const extractInstructorComment = (report) => {
  const comment = String(report?.comment || report?.Comment || '').trim();
  if (comment) {
    return comment;
  }

  const parsedNotes = parseNotesObject(report?.notes);

  if (parsedNotes && typeof parsedNotes.comment === 'string' && parsedNotes.comment.trim()) {
    return parsedNotes.comment.trim();
  }

  if (parsedNotes && typeof parsedNotes.generalNotes === 'string' && parsedNotes.generalNotes.trim()) {
    return parsedNotes.generalNotes.trim();
  }

  // If notes are in the structured report JSON format, avoid showing the raw JSON string.
  if (
    parsedNotes
    && typeof parsedNotes === 'object'
    && (
      Object.prototype.hasOwnProperty.call(parsedNotes, 'isPresent')
      || Object.prototype.hasOwnProperty.call(parsedNotes, 'metrics')
      || Object.prototype.hasOwnProperty.call(parsedNotes, 'comment')
      || Object.prototype.hasOwnProperty.call(parsedNotes, 'generalNotes')
    )
  ) {
    return '';
  }

  if (typeof report?.notes === 'string' && report.notes.trim()) {
    const normalizedRawNotes = report.notes.trim();

    // Ignore raw JSON-like strings that are not valid plain instructor comments.
    if (normalizedRawNotes.startsWith('{') || normalizedRawNotes.startsWith('[')) {
      return '';
    }

    return normalizedRawNotes;
  }

  return '';
};

const formatReportDate = (rawDate) => {
  const parsed = new Date(rawDate || 0);
  if (Number.isNaN(parsed.getTime())) {
    return 'תאריך לא זמין';
  }

  return parsed.toLocaleDateString('he-IL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
};

const toDisplayName = (item) => {
  if (!item || typeof item !== 'object') {
    return '';
  }

  const fullName = String(item?.fullName || '').trim();
  if (fullName) {
    return fullName;
  }

  const firstName = String(item?.firstName || '').trim();
  const lastName = String(item?.lastName || '').trim();
  return `${firstName} ${lastName}`.trim();
};

const normalizeInstructorForLookup = (item) => {
  const instructorId = Number(item?.id || item?.Id || item?.instructorId || item?.InstructorId || 0);
  if (instructorId <= 0) {
    return null;
  }

  const displayName = toDisplayName(item)
    || String(item?.email || item?.Email || '').trim();

  if (!displayName) {
    return null;
  }

  return {
    instructorId,
    displayName,
  };
};

const getLatestInstructorComments = (reports, limit = 3) => {
  if (!Array.isArray(reports) || reports.length === 0) {
    return [];
  }

  const sortedReports = [...reports].sort((a, b) => {
    const firstDate = new Date(b?.reportDate || b?.date || 0).getTime();
    const secondDate = new Date(a?.reportDate || a?.date || 0).getTime();
    return firstDate - secondDate;
  });

  const latestComments = [];

  for (const report of sortedReports) {
    const comment = extractInstructorComment(report);
    if (!comment) {
      continue;
    }

    latestComments.push({
      id: Number(report?.reportId || latestComments.length + 1),
      dateText: formatReportDate(report?.reportDate || report?.date),
      comment,
    });

    if (latestComments.length >= limit) {
      break;
    }
  }

  return latestComments;
};

const escapeHtml = (value) => String(value ?? '')
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;')
  .replace(/'/g, '&#39;');

const SUMMARY_LEAK_MARKERS = [
  'we need to produce',
  "let's craft",
  'lets craft',
  'single paragraph',
  'first sentence',
  'second sentence',
  'third sentence',
  'sentence 1',
  'sentence 2',
  'sentence 3',
  'skip second sentence',
  'all >6',
  'all > 6',
  'hebrew only',
  'instruction',
  'משימה:',
  'המשפט הראשון',
  'המשפט השני',
  'המשפט השלישי',
  'דוגמה לתשובה',
  'תבנית',
  'כתוב פסקת',
  'עברית בלבד',
  'אסור לכתוב רשימות',
  'ילד:',
  'מדדים:',
  'הערות אחרונות:',
];

const containsHebrewCharacters = (text) => /[\u0590-\u05FF]/.test(String(text || ''));
const containsLatinLetters = (text) => /[A-Za-z]/.test(String(text || ''));

const looksLikeSummaryPromptLeak = (text) => {
  const lowered = String(text || '').toLowerCase();
  return SUMMARY_LEAK_MARKERS.some((marker) => lowered.includes(marker.toLowerCase()));
};

const ensureParentRecommendationLastRow = (line) => {
  const normalized = String(line || '').replace(/\s+/g, ' ').trim();
  if (!normalized) {
    return 'להורה מומלץ לקבוע בבית תרגול קצר וקבוע, לחזק הצלחות קטנות ולשמור על אווירה מעודדת.';
  }

  const hasParentKeyword = /להורה|הורה|בבית/.test(normalized);
  const hasRecommendationKeyword = /מומלץ|כדאי|רצוי|המלצה/.test(normalized);

  if (hasParentKeyword && hasRecommendationKeyword) {
    return /[.!?]$/.test(normalized) ? normalized : `${normalized}.`;
  }

  const cleaned = normalized.replace(/^[\-:\s]+/, '').replace(/[.!?]+$/, '');
  return `להורה מומלץ: ${cleaned}.`;
};

const sanitizeAiSummaryForDisplay = (rawSummary) => {
  const cleaned = String(rawSummary || '')
    .replace(/\r/g, '\n')
    .replace(/```[\s\S]*?```/g, ' ')
    .trim();

  if (!cleaned) {
    return '';
  }

  const normalized = cleaned
    .split('\n')
    .map((line) => line.replace(/\s+/g, ' ').trim())
    .filter(Boolean)
    .join('\n');

  const sentenceMatches = normalized.match(/[^.!?\n]+[.!?]?/g) || [];
  const pickedSentences = [];

  for (const rawSentence of sentenceMatches) {
    let sentence = String(rawSentence || '').trim();
    if (!sentence) {
      continue;
    }

    const firstHebrewIndex = sentence.search(/[\u0590-\u05FF]/);
    if (firstHebrewIndex < 0) {
      continue;
    }

    sentence = sentence
      .slice(firstHebrewIndex)
      .trim()
      .replace(/^["'״“”:\-\s]+/, '')
      .replace(/["'״“”\s]+$/, '');

    if (!sentence) {
      continue;
    }

    if (looksLikeSummaryPromptLeak(sentence)) {
      continue;
    }

    if (!containsHebrewCharacters(sentence)) {
      continue;
    }

    if (containsLatinLetters(sentence)) {
      continue;
    }

    pickedSentences.push(sentence);
    if (pickedSentences.length >= 5) {
      break;
    }
  }

  if (pickedSentences.length < 4) {
    return '';
  }

  const normalizedLines = pickedSentences
    .slice(0, 4)
    .map((sentence) => (/[.!?]$/.test(sentence) ? sentence : `${sentence}.`));

  if (normalizedLines.length >= 4) {
    normalizedLines[3] = ensureParentRecommendationLastRow(normalizedLines[3]);
  }

  return normalizedLines.join('\n').trim();
};

const buildPdfReportHtml = ({ childName, instructorName, metrics, latestComments, aiSummary, createdAtText }) => {
  const safeInstructorName = String(instructorName || '').trim() || 'לא זמין';

  const metricRows = metrics.map((item) => {
    const normalizedValue = normalizeProgressValue(Number(item.value));
    const percent = Math.round((normalizedValue / RATING_MAX) * 100);
    const marker = percent > 0 ? '<span class="metric-dot"></span>' : '';

    return `
      <div class="metric-item">
        <div class="metric-head">
          <div class="metric-label">${escapeHtml(item.label)}</div>
          <div class="metric-value">${normalizedValue}/5</div>
        </div>
        <div class="metric-track">
          <div class="metric-fill" style="width:${percent}%;">
            ${marker}
          </div>
        </div>
      </div>
    `;
  }).join('');

  const commentsHtml = latestComments.length > 0
    ? latestComments.map((item) => `
      <div class="comment-item">
        <div class="comment-date">${escapeHtml(item.dateText)}</div>
        <div class="comment-text">${escapeHtml(item.comment).replace(/\n/g, '<br/>')}</div>
      </div>
    `).join('')
    : '<div class="empty">אין הערות מדריך זמינות.</div>';

  const aiSection = aiSummary
    ? `
      <h2>סיכום בינה מלאכותית</h2>
      <div class="ai-box">${escapeHtml(aiSummary).replace(/\n/g, '<br/>')}</div>
    `
    : '';

  return `
    <!doctype html>
    <html lang="he" dir="rtl">
      <head>
        <meta charset="utf-8" />
        <title>דו"ח התקדמות הילד</title>
        <style>
          body {
            font-family: Arial, sans-serif;
            direction: rtl;
            color: #1e3a56;
            padding: 24px;
            line-height: 1.5;
            -webkit-print-color-adjust: exact;
            print-color-adjust: exact;
          }
          h1 {
            margin: 0 0 6px 0;
            color: #1f5e9b;
            font-size: 28px;
          }
          .meta {
            color: #607286;
            font-size: 13px;
            margin-bottom: 16px;
          }
          h2 {
            color: #204a78;
            font-size: 20px;
            margin: 18px 0 8px;
          }

          .metric-item {
            margin-bottom: 12px;
          }
          .metric-head {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 6px;
            gap: 8px;
          }
          .metric-label {
            color: #1f4f81;
            font-size: 14px;
            font-weight: 700;
          }
          .metric-value {
            color: #4f6780;
            font-size: 13px;
            white-space: nowrap;
          }
          .metric-track {
            position: relative;
            height: 10px;
            border-radius: 999px;
            background: #e8f0f8;
            overflow: visible;
            direction: ltr;
          }
          .metric-fill {
            position: relative;
            height: 100%;
            border-radius: 999px;
            background: linear-gradient(90deg, #7cc5ff 0%, #2e77bc 100%);
          }
          .metric-dot {
            position: absolute;
            right: -7px;
            top: 50%;
            width: 14px;
            height: 14px;
            border-radius: 999px;
            background: #2e77bc;
            border: 2px solid #ffffff;
            transform: translateY(-50%);
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.2);
          }

          .comment-item {
            border: 1px solid #dbe8f4;
            border-radius: 10px;
            padding: 8px 10px;
            margin-bottom: 8px;
            background: #f8fbff;
          }
          .comment-date {
            color: #2e77bc;
            font-size: 12px;
            font-weight: 700;
            margin-bottom: 4px;
          }
          .comment-text {
            color: #355974;
            font-size: 14px;
          }
          .ai-box {
            border: 1px solid #dbe8f4;
            border-radius: 10px;
            padding: 10px;
            background: #f8fbff;
            color: #355974;
            font-size: 14px;
          }
          .empty {
            color: #607286;
            font-size: 13px;
          }
        </style>
      </head>
      <body>
        <h1>דו"ח התקדמות הילד</h1>
        <div class="meta">שם הילד: ${escapeHtml(childName)} | מדריך: ${escapeHtml(safeInstructorName)} | הופק בתאריך: ${escapeHtml(createdAtText)}</div>

        <h2>מדדי התקדמות (חציון)</h2>
        ${metricRows}

        <h2>הערות מדריכים אחרונות</h2>
        ${commentsHtml}

        ${aiSection}
      </body>
    </html>
  `;
};

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
  }, [duration, startPosition, translateX]);

  return (
    <Animated.View style={[styles.waveContainer, { transform: [{ translateX }] }]}>
      <Svg width={2400} height={150} viewBox="0 0 2400 150" preserveAspectRatio="none">
        <Path fill={color} d={d} />
      </Svg>
    </Animated.View>
  );
};

const ProgressBar = ({ label, value }) => {
  const normalizedValue = normalizeProgressValue(Number(value));
  const percentFraction = normalizedValue / RATING_MAX;
  const displayPercent = Math.round(percentFraction * 100);

  return (
    <View style={styles.progressItem}>
      <View style={styles.progressLabelRow}>
        <Text style={styles.progressLabel}>{label}</Text>
        <Text style={styles.progressPercent}>{normalizedValue}/5</Text>
      </View>

      <View style={styles.progressTrack}>
        <View style={[styles.progressFill, { width: `${displayPercent}%` }]} />
      </View>
    </View>
  );
};

const ActionButton = ({ label, onPress, disabled = false }) => (
  <PrimaryButton
    label={label}
    style={styles.actionButtonShell}
    gradientStyle={styles.actionButton}
    textStyle={styles.actionButtonText}
    onPress={onPress}
    disabled={disabled}
    colorsOverride={disabled ? ['#9FBBD4', '#8DA8C2'] : ['#2E77BC', '#255E97']}
  />
);

export default function ParentProgressReport({ route, navigation }) {
  const [children, setChildren] = useState([]);
  const [isLoadingChildren, setIsLoadingChildren] = useState(false);
  const [childrenError, setChildrenError] = useState('');
  const [unreadNotificationCount, setUnreadNotificationCount] = useState(0);
  const [reportData, setReportData] = useState(initialRows);
  const [latestInstructorComments, setLatestInstructorComments] = useState([]);
  const [reportInstructorName, setReportInstructorName] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState('');
  const [hasReports, setHasReports] = useState(false);
  const [isGeneratingSummary, setIsGeneratingSummary] = useState(false);
  const [aiSummary, setAiSummary] = useState('');
  const [aiSummarySource, setAiSummarySource] = useState('');
  const [summaryWarning, setSummaryWarning] = useState('');
  const [isSummaryWarningError, setIsSummaryWarningError] = useState(false);

  const authUser = route?.params?.authUser;
  const parentId = Number(route?.params?.parentId || authUser?.id || 0);
  const initialChildId = Number(route?.params?.selectedChildId || route?.params?.child?.id || route?.params?.childId || 0);
  const initialChildFromRoute = route?.params?.child || null;

  const [selectedChildId, setSelectedChildId] = useState(initialChildId > 0 ? initialChildId : null);

  const selectedChild = useMemo(
    () => children.find((item) => Number(item?.id || 0) === Number(selectedChildId || 0)) || null,
    [children, selectedChildId],
  );

  const childName = useMemo(() => {
    const selectedName = toDisplayName(selectedChild);
    if (selectedName) {
      return selectedName;
    }

    const routeChildName = toDisplayName(initialChildFromRoute);
    return routeChildName || 'ילד שלא נבחר';
  }, [selectedChild, initialChildFromRoute]);

  const aiSummaryLines = useMemo(
    () => String(aiSummary || '')
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter(Boolean)
      .slice(0, 4),
    [aiSummary],
  );

  const resolveInstructorNameForReports = useCallback(async (reports) => {
    if (!parentId || !selectedChildId || !Array.isArray(reports) || reports.length === 0) {
      return '';
    }

    try {
      const response = await fetch(`${API_BASE_URL}/chat/parent/${parentId}/children/${selectedChildId}/instructors`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        return '';
      }

      const instructors = (Array.isArray(payload) ? payload : [])
        .map(normalizeInstructorForLookup)
        .filter((item) => item && item.instructorId > 0 && item.displayName);

      if (instructors.length === 0) {
        return '';
      }

      const orderedReports = [...reports].sort((first, second) => {
        const firstDate = new Date(first?.reportDate || first?.ReportDate || first?.date || first?.Date || 0).getTime();
        const secondDate = new Date(second?.reportDate || second?.ReportDate || second?.date || second?.Date || 0).getTime();

        if (firstDate !== secondDate) {
          return secondDate - firstDate;
        }

        const firstId = Number(first?.reportId || first?.ReportId || 0);
        const secondId = Number(second?.reportId || second?.ReportId || 0);
        return secondId - firstId;
      });

      const latestInstructorId = Number(orderedReports[0]?.instructorId || orderedReports[0]?.InstructorId || 0);
      if (latestInstructorId > 0) {
        const matchingInstructor = instructors.find((item) => item.instructorId === latestInstructorId);
        if (matchingInstructor?.displayName) {
          return matchingInstructor.displayName;
        }
      }

      if (instructors.length === 1) {
        return instructors[0].displayName;
      }

      return '';
    } catch (_error) {
      return '';
    }
  }, [parentId, selectedChildId]);

  const loadUnreadNotifications = useCallback(async () => {
    if (!parentId) {
      setUnreadNotificationCount(0);
      return;
    }

    try {
      const notifications = await fetchParentNotifications(parentId);
      setUnreadNotificationCount(countUnreadNotifications(notifications));
    } catch (_error) {
      setUnreadNotificationCount(0);
    }
  }, [parentId]);

  useEffect(() => {
    loadUnreadNotifications();
  }, [loadUnreadNotifications]);

  useFocusEffect(
    useCallback(() => {
      loadUnreadNotifications();
    }, [loadUnreadNotifications]),
  );

  useEffect(() => {
    if (!parentId) {
      return undefined;
    }

    const intervalId = setInterval(() => {
      loadUnreadNotifications();
    }, 10000);

    return () => {
      clearInterval(intervalId);
    };
  }, [parentId, loadUnreadNotifications]);

  const handleBackPress = useCallback(() => {
    if (navigation.canGoBack()) {
      navigation.goBack();
      return;
    }

    navigation.navigate('ParentHomepage', { authUser, parentId });
  }, [navigation, authUser, parentId]);

  const openNotificationsInbox = useCallback(() => {
    navigation.navigate('ParentNotificationsInbox', { authUser, parentId });
  }, [navigation, authUser, parentId]);

  useEffect(() => {
    let isCancelled = false;

    const loadChildren = async () => {
      if (!parentId) {
        if (!isCancelled) {
          setChildren([]);
          setSelectedChildId(null);
          setChildrenError('לא זוהה הורה מחובר. התחברו מחדש.');
          setIsLoadingChildren(false);
        }
        return;
      }

      try {
        setIsLoadingChildren(true);
        setChildrenError('');

        const response = await fetch(`${API_BASE_URL}/chat/parent/${parentId}/children`);
        const payload = await response.json().catch(() => null);

        if (!response.ok) {
          throw new Error(payload?.message || 'לא ניתן לטעון את רשימת הילדים.');
        }

        if (isCancelled) {
          return;
        }

        const normalizedChildren = Array.isArray(payload) ? payload : [];
        setChildren(normalizedChildren);

        setSelectedChildId((currentSelectedId) => {
          const safeCurrent = Number(currentSelectedId || 0);
          if (safeCurrent > 0 && normalizedChildren.some((item) => Number(item?.id || 0) === safeCurrent)) {
            return safeCurrent;
          }

          if (initialChildId > 0 && normalizedChildren.some((item) => Number(item?.id || 0) === initialChildId)) {
            return initialChildId;
          }

          const firstChildId = Number(normalizedChildren[0]?.id || 0);
          return firstChildId > 0 ? firstChildId : null;
        });
      } catch (error) {
        if (!isCancelled) {
          setChildren([]);
          setSelectedChildId(null);
          setChildrenError(error?.message || 'לא ניתן לטעון את רשימת הילדים.');
        }
      } finally {
        if (!isCancelled) {
          setIsLoadingChildren(false);
        }
      }
    };

    loadChildren();

    return () => {
      isCancelled = true;
    };
  }, [parentId, initialChildId]);

  const applyServerMedianMetrics = (serverMetrics) => {
    if (!Array.isArray(serverMetrics) || serverMetrics.length === 0) return;

    const nextMetrics = initialRows.map((metric, index) => {
      const targetLabel = String(metric?.label || '').trim();

      const directMatch = serverMetrics.find(
        (item) => String(item?.label || '').trim() === targetLabel,
      );

      const byIndex = serverMetrics[index];
      const picked = directMatch ?? byIndex;
      const pickedValue = Number(picked?.value);

      if (!Number.isFinite(pickedValue)) {
        return metric;
      }

      return {
        ...metric,
        value: normalizeProgressValue(pickedValue),
      };
    });

    setReportData(nextMetrics);
  };

  const handleGenerateSummary = async () => {
    if (!parentId || !selectedChildId || isGeneratingSummary || !hasReports) {
      return;
    }

    try {
      setIsGeneratingSummary(true);
      setSummaryWarning('');
      setIsSummaryWarningError(false);

      const response = await fetch(`${API_BASE_URL}/parent/${parentId}/children/${selectedChildId}/reports/summary`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          metrics: reportData.map((metric) => ({
            label: metric.label,
            value: Number(metric.value),
          })),
        }),
      });

      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לייצר סיכום כרגע.');
      }

      const summaryText = String(payload?.summary || '').trim();
      const sanitizedSummary = sanitizeAiSummaryForDisplay(summaryText);
      if (!sanitizedSummary) {
        throw new Error('לא התקבל סיכום מהשרת.');
      }

      const summarySource = String(payload?.source || '').trim().toLowerCase();

      setAiSummary(sanitizedSummary);
      setAiSummarySource(summarySource);
      setSummaryWarning('');
      setIsSummaryWarningError(false);
      applyServerMedianMetrics(payload?.medianMetrics);
    } catch (error) {
      setSummaryWarning(error?.message || 'הבינה המלאכותית לא עבדה כרגע. בבקשה נסו שוב בעוד רגע.');
      setIsSummaryWarningError(true);
    } finally {
      setIsGeneratingSummary(false);
    }
  };

  const handleDownloadPdf = async () => {
    if (!hasReports) {
      Alert.alert('אין נתונים', 'לא קיימים דיווחים ליצירת PDF עבור ילד זה.');
      return;
    }

    try {
      const createdAtText = new Date().toLocaleString('he-IL', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      });

      const html = buildPdfReportHtml({
        childName,
        instructorName: reportInstructorName,
        metrics: reportData,
        latestComments: latestInstructorComments,
        aiSummary: aiSummary.trim(),
        createdAtText,
      });

      if (Platform.OS === 'web') {
        try {
          await Print.printAsync({ html });
        } catch (_printError) {
          const popupWindow = globalThis?.window?.open('', '_blank', 'noopener,noreferrer');
          if (!popupWindow) {
            throw new Error('לא ניתן לפתוח חלון הדפסה בדפדפן (ייתכן שחסימת חלונות קופצים פעילה).');
          }

          popupWindow.document.open();
          popupWindow.document.write(html);
          popupWindow.document.close();
          popupWindow.focus();
          popupWindow.print();
        }

        return;
      }

      const { uri } = await Print.printToFileAsync({ html });
      const canShare = await Sharing.isAvailableAsync();

      if (!canShare) {
        await Print.printAsync({ html });
        return;
      }

      await Sharing.shareAsync(uri, {
        mimeType: 'application/pdf',
        dialogTitle: 'הורדת דו"ח PDF',
        UTI: 'com.adobe.pdf',
      });
    } catch (error) {
      const details = String(error?.message || '').trim();
      const baseMessage = Platform.OS === 'web'
        ? 'לא ניתן לפתוח את חלון ההדפסה בדפדפן כרגע.'
        : 'לא ניתן ליצור את קובץ ה-PDF כרגע. נסו שוב בעוד רגע.';

      Alert.alert('שגיאה', details ? `${baseMessage}\n\nפרטים: ${details}` : baseMessage);
    }
  };

  useEffect(() => {
    let isCancelled = false;

    const fetchReports = async () => {
      if (!parentId) {
        setIsLoading(false);
        setErrorMessage('לא זוהה הורה מחובר. התחברו מחדש.');
        setHasReports(false);
        setReportData(initialRows);
        setLatestInstructorComments([]);
        setReportInstructorName('');
        setAiSummary('');
        setAiSummarySource('');
        setSummaryWarning('');
        setIsSummaryWarningError(false);
        return;
      }

      if (isLoadingChildren) {
        return;
      }

      if (!selectedChildId) {
        setIsLoading(false);
        setErrorMessage(children.length === 0 ? 'לא נמצאו ילדים פעילים להצגת דו"ח.' : 'יש לבחור ילד כדי להציג דו"ח.');
        setHasReports(false);
        setReportData(initialRows);
        setLatestInstructorComments([]);
        setReportInstructorName('');
        setAiSummary('');
        setAiSummarySource('');
        setSummaryWarning('');
        setIsSummaryWarningError(false);
        return;
      }

      try {
        setIsLoading(true);
        setErrorMessage('');
        setAiSummary('');
        setAiSummarySource('');
        setSummaryWarning('');
        setIsSummaryWarningError(false);

        const response = await fetch(`${API_BASE_URL}/parent/${parentId}/children/${selectedChildId}/reports`);
        const payload = await response.json().catch(() => null);

        if (!response.ok) {
          throw new Error(payload?.message || 'לא ניתן לטעון את דו"ח ההתקדמות.');
        }

        const reports = Array.isArray(payload) ? payload : [];
        if (!isCancelled) {
          if (reports.length === 0) {
            setHasReports(false);
            setErrorMessage('עדיין אין דיווחים מתועדים לילד זה.');
            setReportData(initialRows);
            setLatestInstructorComments([]);
            setReportInstructorName('');
          } else {
            const resolvedInstructorName = await resolveInstructorNameForReports(reports);
            if (isCancelled) {
              return;
            }

            setHasReports(true);
            setReportData(buildMedianMetrics(reports, initialRows));
            setLatestInstructorComments(getLatestInstructorComments(reports, 3));
            setReportInstructorName(resolvedInstructorName);
          }
        }
      } catch (error) {
        if (!isCancelled) {
          setHasReports(false);
          setReportData(initialRows);
          setLatestInstructorComments([]);
          setReportInstructorName('');
          setErrorMessage(error?.message || 'שגיאה בעת חישוב מדדים כרגע.');
        }
      } finally {
        if (!isCancelled) {
          setIsLoading(false);
        }
      }
    };

    fetchReports();

    return () => {
      isCancelled = true;
    };
  }, [parentId, selectedChildId, isLoadingChildren, children.length, resolveInstructorNameForReports]);

  return (
    <View style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="transparent" translucent />

      <LinearGradient
        colors={['#005a80', '#00456a', colors.bgDeep]}
        style={StyleSheet.absoluteFill}
      />

      <View style={StyleSheet.absoluteFill}>
        <LinearGradient
          colors={['#00aed8', '#007fa7', '#004d73']}
          style={StyleSheet.absoluteFill}
        />
      </View>

      <LinearGradient
        colors={['rgba(0, 212, 255, 0.45)', 'rgba(0, 140, 175, 0.22)', 'rgba(0, 68, 102, 0)']}
        style={styles.surfaceGlow}
      />


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

      <BlurView intensity={20} tint="dark" style={styles.navbar}>
        <TouchableOpacity
          style={styles.logoutBtn}
          activeOpacity={0.7}
          onPress={handleBackPress}
        >
          <Text style={styles.logoutBtnText}>‹</Text>
        </TouchableOpacity>

        <View style={styles.navCenter}>
          <LinearGradient colors={['#00d4ff', '#0099cc']} style={styles.navLogo}>
            <Text style={styles.navLogoEmoji}>📊</Text>
          </LinearGradient>
          <Text style={styles.navTitle}>דו״ח התקדמות</Text>
        </View>

        <View style={{ width: 42 }} />
      </BlurView>

      <ScrollView contentContainerStyle={styles.scrollContent} showsVerticalScrollIndicator={false}>
        <View style={styles.hero}>
          <Text style={styles.heroTitle}>דו״ח התקדמות מפורט</Text>
          <Text style={styles.heroSubtitle}>ניתוח ביצועי ילד לפי תרגילי השחייה</Text>
          <Text style={styles.heroChildName}>{childName}</Text>
        </View>

        <View style={styles.section}>
          <View style={styles.sectionHeaderRow}>
            <Text style={styles.sectionTitle}>בחירת ילד לדו״ח</Text>
          </View>

          <BlurView intensity={18} tint="dark" style={styles.glassCard}>
            {isLoadingChildren ? (
              <View style={styles.childPickerLoadingRow}>
                <ActivityIndicator size="small" color="#00d4ff" />
                <Text style={styles.childPickerStateText}>טוען ילדים...</Text>
              </View>
            ) : childrenError ? (
              <Text style={styles.errorText}>{childrenError}</Text>
            ) : children.length === 0 ? (
              <Text style={styles.stateText}>לא נמצאו ילדים פעילים להצגת דו״ח.</Text>
            ) : (
              <View style={styles.childChipsWrap}>
                {children.map((childItem) => {
                  const childId = Number(childItem?.id || 0);
                  if (childId <= 0) {
                    return null;
                  }

                  const isSelected = childId === Number(selectedChildId || 0);

                  return (
                    <TouchableOpacity
                      key={`progress-child-${childId}`}
                      style={[styles.childChip, isSelected && styles.childChipActive]}
                      activeOpacity={0.85}
                      onPress={() => setSelectedChildId(childId)}
                    >
                      <Text style={[styles.childChipText, isSelected && styles.childChipTextActive]}>
                        {toDisplayName(childItem) || `ילד #${childId}`}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>
            )}
          </BlurView>
        </View>

        <View style={styles.section}>
          <View style={styles.sectionHeaderRow}>
            <Text style={styles.sectionTitle}>מדדי התקדמות</Text>
          </View>

          <BlurView intensity={18} tint="dark" style={styles.glassCard}>
            <Text style={styles.cardMeta}>תאריכים: שקלול כלל הדיווחים</Text>

            {isLoading ? (
              <ActivityIndicator size="large" color="#00d4ff" style={{ marginVertical: 20 }} />
            ) : errorMessage ? (
              <Text style={styles.errorText}>{errorMessage}</Text>
            ) : (
              reportData.map((item) => (
                <ProgressBar
                  key={item.label}
                  label={item.label}
                  value={item.value}
                />
              ))
            )}
          </BlurView>
        </View>

        <View style={styles.section}>
          <View style={styles.sectionHeaderRow}>
            <Text style={styles.sectionTitle}>הערות מדריכים אחרונות</Text>
          </View>

          <BlurView intensity={18} tint="dark" style={styles.glassCard}>
            {latestInstructorComments.length === 0 ? (
              <Text style={styles.stateText}>עדיין אין הערות מדריך להצגה.</Text>
            ) : (
              latestInstructorComments.map((item) => (
                <View key={`${item.id}-${item.dateText}`} style={styles.commentItem}>
                  <Text style={styles.commentDate}>{item.dateText}</Text>
                  <Text style={styles.commentText}>{item.comment}</Text>
                </View>
              ))
            )}
          </BlurView>
        </View>

        <View style={styles.actionsSection}>

          <PrimaryButton
            label={isGeneratingSummary ? 'מייצר סיכום...' : 'תן לבינה המלאכותית לסכם את התקדמות הילד'}
            style={styles.summaryButtonShell}
            gradientStyle={styles.summaryButton}
            textStyle={styles.summaryButtonText}
            onPress={handleGenerateSummary}
            disabled={isGeneratingSummary || isLoading || !hasReports}
            colorsOverride={
              (isGeneratingSummary || isLoading || !hasReports)
                ? ['#9FBBD4', '#8DA8C2']
                : ['#2E77BC', '#255E97']
            }
          />

          {summaryWarning ? (
            <Text style={isSummaryWarningError ? styles.summaryErrorText : styles.summaryNoticeText}>{summaryWarning}</Text>
          ) : null}

          {aiSummary ? (
            <BlurView intensity={18} tint="dark" style={styles.summaryBox}>
              <Text style={styles.summarySourceLabel}>
                {aiSummarySource === 'ai' ? 'סיכום שנוצר בעזרת בינה מלאכותית' : 'סיכום גיבוי מבוסס נתונים'}
              </Text>

              <View style={styles.summaryLinesWrap}>
                {aiSummaryLines.map((line, index) => (
                  <View
                    key={`summary-line-${index}`}
                    style={[
                      styles.summaryLineRow,
                      index === aiSummaryLines.length - 1 && styles.summaryLineLast,
                    ]}
                  >
                    <View style={styles.summaryLineBadge}>
                      <Text style={styles.summaryLineBadgeText}>{index + 1}</Text>
                    </View>

                    <Text style={styles.summaryLineText}>{line}</Text>
                  </View>
                ))}
              </View>
            </BlurView>
          ) : null}

          <ActionButton
            label="הורדת דו״ח PDF"
            onPress={handleDownloadPdf}
            disabled={isLoading || !hasReports}
          />
        </View>

        <View style={styles.footer}>
          <Text style={styles.footerCopy}>כל הזכויות שמורות ל-ACM 2026 ©</Text>
        </View>
      </ScrollView>
    </View>
  );
}
