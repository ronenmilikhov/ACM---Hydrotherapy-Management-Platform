import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  I18nManager,
  Modal,
  Platform,
  SafeAreaView,
  ScrollView,
  StatusBar,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from 'react-native';
import DateTimePicker, { DateTimePickerAndroid } from '@react-native-community/datetimepicker';
import { useFocusEffect } from '@react-navigation/native';
import AquaticBackground from '../components/ui/AquaticBackground';
import RoleHeader from '../components/ui/RoleHeader';
import PrimaryButton from '../components/ui/PrimaryButton';
import { API_BASE_URL } from '../apiConfig';

const defaultFormState = {
  mode: 'private',
  meetingDate: '',
  startTime: '',
  endTime: '',
  groupId: '',
  capacity: '1',
  minRegistrations: '1',
  generalNote: '',
  targetMetric: '',
};

const METRIC_OPTIONS = [
  { value: 'ציפה על הבטן', label: 'ציפה על הבטן' },
  { value: 'בעיטות בטן', label: 'בעיטות בטן' },
  { value: 'קפיצה חץ', label: 'קפיצה חץ' },
  { value: 'חישוק', label: 'חישוק' },
  { value: 'ציפה על הגב', label: 'ציפה על הגב' },
  { value: 'בעיטות גב', label: 'בעיטות גב' },
  { value: 'קפיצה עמוק', label: 'קפיצה עמוק' },
  { value: 'ביטחון במים', label: 'ביטחון במים' },
];

const getIsraelParts = (dateValue) => {
  if (!(dateValue instanceof Date) || Number.isNaN(dateValue.getTime())) {
    return null;
  }
  try {
    const formatter = new Intl.DateTimeFormat('en-US', {
      timeZone: 'Asia/Jerusalem',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
    const parts = formatter.formatToParts(dateValue);
    const getVal = (type) => parts.find(p => p.type === type)?.value;
    
    const year = getVal('year');
    const month = getVal('month');
    const day = getVal('day');
    const hour = getVal('hour');
    const minute = getVal('minute');
    
    if (year && month && day && hour !== undefined && minute !== undefined) {
      return {
        year: Number(year),
        month: Number(month),
        day: Number(day),
        hour: Number(hour) % 24,
        minute: Number(minute),
      };
    }
  } catch (e) {
    console.warn('Failed to format with Asia/Jerusalem timezone, falling back to local time:', e);
  }
  
  return {
    year: dateValue.getFullYear(),
    month: dateValue.getMonth() + 1,
    day: dateValue.getDate(),
    hour: dateValue.getHours(),
    minute: dateValue.getMinutes(),
  };
};

const createDateFromIsraelParts = (year, month, day, hour = 0, minute = 0) => {
  const utcDate = new Date(Date.UTC(year, month - 1, day, hour, minute));
  const parts = getIsraelParts(utcDate);
  if (!parts) {
    return utcDate;
  }
  const utcMinutes = Date.UTC(year, month - 1, day, hour, minute) / 60000;
  const localMidnightMinutes = Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute) / 60000;
  const offsetMinutes = localMidnightMinutes - utcMinutes;
  return new Date((utcMinutes - offsetMinutes) * 60000);
};

const toIsoDateString = (dateValue) => {
  const parts = getIsraelParts(dateValue);
  if (!parts) {
    return '';
  }
  const year = parts.year;
  const month = String(parts.month).padStart(2, '0');
  const day = String(parts.day).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const toTimeIsoString = (dateValue) => {
  const parts = getIsraelParts(dateValue);
  if (!parts) {
    return '';
  }
  const hours = String(parts.hour).padStart(2, '0');
  const minutes = String(parts.minute).padStart(2, '0');
  return `${hours}:${minutes}`;
};

const parseTimeToDate = (timeString) => {
  const normalized = String(timeString || '').trim();
  const now = new Date();
  const parts = getIsraelParts(now);
  if (!/^\d{2}:\d{2}$/.test(normalized)) {
    return createDateFromIsraelParts(parts.year, parts.month, parts.day, parts.hour, parts.minute);
  }
  const [hoursText, minutesText] = normalized.split(':');
  return createDateFromIsraelParts(parts.year, parts.month, parts.day, Number(hoursText), Number(minutesText));
};

const parseIsoDate = (rawValue) => {
  const normalized = String(rawValue || '').trim();
  if (!/^\d{4}-\d{2}-\d{2}$/.test(normalized)) {
    return null;
  }
  const [yearText, monthText, dayText] = normalized.split('-');
  return createDateFromIsraelParts(Number(yearText), Number(monthText), Number(dayText), 0, 0);
};

const formatDisplayDate = (rawDate) => {
  const parsedDate = parseIsoDate(rawDate);
  if (!parsedDate) {
    return '';
  }
  const parts = getIsraelParts(parsedDate);
  if (!parts) {
    return '';
  }
  const day = String(parts.day).padStart(2, '0');
  const month = String(parts.month).padStart(2, '0');
  const year = parts.year;
  return `${day}/${month}/${year}`;
};

const parseHourMinuteToMinutes = (rawValue) => {
  const normalized = String(rawValue || '').trim();
  if (!/^\d{2}:\d{2}$/.test(normalized)) {
    return null;
  }

  const [hoursText, minutesText] = normalized.split(':');
  const hours = Number(hoursText);
  const minutes = Number(minutesText);

  if (!Number.isInteger(hours) || !Number.isInteger(minutes)) {
    return null;
  }

  if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59) {
    return null;
  }

  return (hours * 60) + minutes;
};

const buildComparableMinuteStamp = (isoDate, minutesFromMidnight) => {
  const normalizedDate = String(isoDate || '').trim();
  if (!/^\d{4}-\d{2}-\d{2}$/.test(normalizedDate) || !Number.isInteger(minutesFromMidnight)) {
    return null;
  }

  const [yearText, monthText, dayText] = normalizedDate.split('-');
  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);

  if (!Number.isInteger(year) || !Number.isInteger(month) || !Number.isInteger(day)) {
    return null;
  }

  const dateMidnight = createDateFromIsraelParts(year, month, day, 0, 0);
  return Math.floor(dateMidnight.getTime() / 60000) + minutesFromMidnight;
};

const getIsraelNowMinuteStamp = () => {
  const now = new Date();
  const parts = getIsraelParts(now);
  const startOfDay = createDateFromIsraelParts(parts.year, parts.month, parts.day, 0, 0);
  return Math.floor(startOfDay.getTime() / 60000) + (parts.hour * 60) + parts.minute;
};

const getIsraelTodayIsoDate = () => {
  const now = new Date();
  const parts = getIsraelParts(now);
  const year = parts.year;
  const month = String(parts.month).padStart(2, '0');
  const day = String(parts.day).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const recipientKey = (parentId, childId) => `${parentId}-${childId}`;

const NUMBER_OPTIONS = Array.from({ length: 19 }, (_, i) => i + 2);

export default function InstructorLessonScheduler({ navigation, route }) {
  const authUser = route?.params?.authUser;
  const instructorId = Number(authUser?.id || 0);

  const [form, setForm] = useState(defaultFormState);
  const [selectedRecipients, setSelectedRecipients] = useState({});
  const [recipientPool, setRecipientPool] = useState([]);
  const [recipientDropdownOpen, setRecipientDropdownOpen] = useState(false);
  const [draftRecipientKey, setDraftRecipientKey] = useState('');
  const [instructorGroups, setInstructorGroups] = useState([]);
  const [showGroupDropdown, setShowGroupDropdown] = useState(false);
  const [showMetricDropdown, setShowMetricDropdown] = useState(false);
  const [showDateModal, setShowDateModal] = useState(false);
  const [draftMeetingDate, setDraftMeetingDate] = useState(new Date());
  const draftMeetingDateRef = useRef(new Date());
  const [showStartTimeModal, setShowStartTimeModal] = useState(false);
  const [draftStartTime, setDraftStartTime] = useState(new Date());
  const draftStartTimeRef = useRef(new Date());
  const [showEndTimeModal, setShowEndTimeModal] = useState(false);
  const [draftEndTime, setDraftEndTime] = useState(new Date());
  const draftEndTimeRef = useRef(new Date());
  const [groupChildrenKeys, setGroupChildrenKeys] = useState(new Set());
  const [showCapacityModal, setShowCapacityModal] = useState(false);
  const [showMinRegistrationsModal, setShowMinRegistrationsModal] = useState(false);

  const [isLoadingPool, setIsLoadingPool] = useState(false);
  const [isLoadingGroups, setIsLoadingGroups] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorText, setErrorText] = useState('');
  const [successText, setSuccessText] = useState('');

  const selectedRecipientItems = useMemo(() => {
    const chosenKeys = new Set(Object.keys(selectedRecipients).filter((key) => selectedRecipients[key]));
    let pool = recipientPool;
    if (form.mode === 'group') {
      if (!form.groupId) {
        return [];
      }
      pool = pool.filter((item) => groupChildrenKeys.has(recipientKey(item.parentId, item.childId)));
    }
    return pool.filter((item) => chosenKeys.has(recipientKey(item.parentId, item.childId)));
  }, [recipientPool, selectedRecipients, form.mode, form.groupId, groupChildrenKeys]);

  const visibleRecipientOptions = useMemo(() => {
    if (form.mode === 'group') {
      if (!form.groupId) {
        return [];
      }
      return recipientPool.filter((item) => groupChildrenKeys.has(recipientKey(item.parentId, item.childId)));
    }
    return recipientPool;
  }, [recipientPool, form.mode, form.groupId, groupChildrenKeys]);

  const draftRecipientOption = useMemo(() => {
    if (!draftRecipientKey) {
      return null;
    }

    return recipientPool.find((item) => recipientKey(item.parentId, item.childId) === draftRecipientKey) || null;
  }, [recipientPool, draftRecipientKey]);

  const selectedGroup = useMemo(() => {
    const normalizedGroupId = Number(form.groupId || 0);
    if (!normalizedGroupId) {
      return null;
    }

    return instructorGroups.find((group) => Number(group.groupId) === normalizedGroupId) || null;
  }, [instructorGroups, form.groupId]);

  const resetStatusMessages = () => {
    setErrorText('');
    setSuccessText('');
  };

  const loadRecipientPool = useCallback(async () => {
    if (!instructorId) {
      setRecipientPool([]);
      return;
    }

    try {
      setIsLoadingPool(true);
      const response = await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/recipients`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון רשימת הורים וילדים כרגע.');
      }
      setRecipientPool(Array.isArray(payload) ? payload : []);
    } catch (error) {
      setRecipientPool([]);
      setErrorText(error?.message || 'לא ניתן לטעון רשימת הורים וילדים כרגע.');
    } finally {
      setIsLoadingPool(false);
    }
  }, [instructorId]);

  const loadInstructorGroups = useCallback(async () => {
    if (!instructorId) {
      setInstructorGroups([]);
      return;
    }

    try {
      setIsLoadingGroups(true);
      const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/groups`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון קבוצות כרגע.');
      }

      const normalizedGroups = (Array.isArray(payload) ? payload : [])
        .map((group) => ({
          groupId: Number(group?.groupId || 0),
          name: String(group?.name || '').trim(),
        }))
        .filter((group) => group.groupId > 0)
        .sort((a, b) => a.name.localeCompare(b.name, 'he'));

      setInstructorGroups(normalizedGroups);
    } catch (error) {
      setInstructorGroups([]);
      setErrorText(error?.message || 'לא ניתן לטעון קבוצות כרגע.');
    } finally {
      setIsLoadingGroups(false);
    }
  }, [instructorId]);

  const refreshAll = useCallback(async () => {
    await Promise.all([loadRecipientPool(), loadInstructorGroups()]);
  }, [loadRecipientPool, loadInstructorGroups]);

  useEffect(() => {
    refreshAll();
  }, [refreshAll]);

  useFocusEffect(
    useCallback(() => {
      refreshAll();
    }, [refreshAll]),
  );

  useEffect(() => {
    if (form.mode !== 'group') {
      setGroupChildrenKeys(new Set());
      return;
    }

    if (!form.groupId) {
      setGroupChildrenKeys(new Set());
      setSelectedRecipients({});
      setDraftRecipientKey('');
      return;
    }

    const fetchGroupChildren = async () => {
      try {
        setErrorText('');
        const response = await fetch(`${API_BASE_URL}/instructor/${instructorId}/groups/${form.groupId}/children`);
        const payload = await response.json().catch(() => null);
        if (response.ok && payload && Array.isArray(payload.children)) {
          const groupRecipients = payload.children
            .filter(c => c.parentId && c.childId)
            .map(c => ({
              parentId: c.parentId,
              childId: c.childId,
              parentFullName: c.parentFullName || '',
              parentEmail: c.parentEmail || '',
              childFullName: c.fullName || `${c.firstName} ${c.lastName}`.trim()
            }));

          setRecipientPool(prevPool => {
            const nextPool = [...prevPool];
            groupRecipients.forEach(item => {
              const exists = nextPool.some(p => p.parentId === item.parentId && p.childId === item.childId);
              if (!exists) {
                nextPool.push(item);
              }
            });
            return nextPool;
          });

          const keysSet = new Set();
          const nextSelected = {};
          groupRecipients.forEach(item => {
            const key = recipientKey(item.parentId, item.childId);
            keysSet.add(key);
            nextSelected[key] = true;
          });
          setGroupChildrenKeys(keysSet);
          setSelectedRecipients(nextSelected);
        }
      } catch (err) {
        console.error('Failed to load group children:', err);
      }
    };

    fetchGroupChildren();
  }, [form.groupId, form.mode, instructorId]);

  const onModeChange = (mode) => {
    if (mode === form.mode) {
      return;
    }

    setForm((prev) => ({
      ...prev,
      mode,
      capacity: mode === 'private' ? '1' : (Number(prev.capacity) >= 2 ? prev.capacity : '20'),
      minRegistrations: mode === 'private' ? '1' : (Number(prev.minRegistrations) >= 2 ? prev.minRegistrations : '2'),
      groupId: mode === 'private' ? '' : prev.groupId,
    }));
    setShowGroupDropdown(false);
    setShowMetricDropdown(false);

    if (mode === 'private') {
      const firstSelectedKey = Object.keys(selectedRecipients).find((key) => selectedRecipients[key]);
      setSelectedRecipients(firstSelectedKey ? { [firstSelectedKey]: true } : {});
      setDraftRecipientKey(firstSelectedKey || '');
    } else {
      setDraftRecipientKey('');
    }

    setRecipientDropdownOpen(false);
    resetStatusMessages();
  };

  const addRecipientFromDropdown = () => {
    if (!draftRecipientKey) {
      setErrorText('יש לבחור הורה וילד לפני הוספה.');
      return;
    }

    setErrorText('');
    setSuccessText('');

    if (form.mode === 'private') {
      setSelectedRecipients({ [draftRecipientKey]: true });
      setRecipientDropdownOpen(false);
      return;
    }

    setSelectedRecipients((prev) => ({
      ...prev,
      [draftRecipientKey]: true,
    }));
    setDraftRecipientKey('');
    setRecipientDropdownOpen(false);
  };

  const removeSelectedRecipient = (parentId, childId) => {
    const key = recipientKey(parentId, childId);
    setSelectedRecipients((prev) => {
      if (!prev[key]) {
        return prev;
      }

      const next = { ...prev };
      delete next[key];
      return next;
    });

    if (draftRecipientKey === key) {
      setDraftRecipientKey('');
    }

    setErrorText('');
    setSuccessText('');
  };

  const openMeetingDatePicker = () => {
    const israelTodayIsoDate = getIsraelTodayIsoDate();
    const fallbackTodayDate = parseIsoDate(israelTodayIsoDate) || new Date();
    const requestedDate = parseIsoDate(form.meetingDate) || fallbackTodayDate;
    const today = new Date();
    const todayParts = getIsraelParts(today);
    const todayAtMidnight = createDateFromIsraelParts(todayParts.year, todayParts.month, todayParts.day, 0, 0);
    const minimumDate = parseIsoDate(israelTodayIsoDate) || todayAtMidnight;
    const existingDate = requestedDate < minimumDate ? minimumDate : requestedDate;

    if (Platform.OS === 'android') {
      DateTimePickerAndroid.open({
        value: existingDate,
        mode: 'date',
        display: 'calendar',
        minimumDate,
        maximumDate: new Date(2100, 11, 31),
        onChange: (event, selectedDate) => {
          if (event.type === 'set' && selectedDate) {
            setForm((prev) => ({ ...prev, meetingDate: toIsoDateString(selectedDate) }));
          }
        },
      });
      return;
    }

    draftMeetingDateRef.current = existingDate;
    setDraftMeetingDate(existingDate);
    setShowDateModal(true);
  };

  const saveMeetingDateFromModal = () => {
    setForm((prev) => ({ ...prev, meetingDate: toIsoDateString(draftMeetingDateRef.current) }));
    setShowDateModal(false);
  };

  const getIsraelNowParts = () => {
    const now = new Date();
    const parts = getIsraelParts(now);
    const year = parts.year;
    const month = String(parts.month).padStart(2, '0');
    const day = String(parts.day).padStart(2, '0');
    return {
      date: `${year}-${month}-${day}`,
      hour: parts.hour,
      minute: parts.minute,
    };
  };

  const openStartTimePicker = () => {
    let pickerValue;
    if (form.startTime && /^\d{2}:\d{2}$/.test(form.startTime)) {
      const [h, m] = form.startTime.split(':').map(Number);
      const d = new Date();
      d.setHours(h, m, 0, 0);
      pickerValue = d;
    } else {
      pickerValue = new Date();
    }

    if (Platform.OS === 'android') {
      DateTimePickerAndroid.open({
        value: pickerValue,
        mode: 'time',
        display: 'spinner',
        is24Hour: true,
        onChange: (event, selectedDate) => {
          if (event.type === 'set' && selectedDate) {
            const h = String(selectedDate.getHours()).padStart(2, '0');
            const m = String(selectedDate.getMinutes()).padStart(2, '0');
            setForm((prev) => ({ ...prev, startTime: `${h}:${m}` }));
          }
        },
      });
      return;
    }

    draftStartTimeRef.current = pickerValue;
    setDraftStartTime(pickerValue);
    setShowStartTimeModal(true);
  };

  const openEndTimePicker = () => {
    let pickerValue;
    if (form.endTime && /^\d{2}:\d{2}$/.test(form.endTime)) {
      const [h, m] = form.endTime.split(':').map(Number);
      const d = new Date();
      d.setHours(h, m, 0, 0);
      pickerValue = d;
    } else if (form.startTime && /^\d{2}:\d{2}$/.test(form.startTime)) {
      const [h, m] = form.startTime.split(':').map(Number);
      let endMinutes = (h * 60 + m) + 60;
      if (endMinutes >= 24 * 60) endMinutes = 24 * 60 - 5;
      const d = new Date();
      d.setHours(Math.floor(endMinutes / 60), endMinutes % 60, 0, 0);
      pickerValue = d;
    } else {
      pickerValue = new Date();
    }

    if (Platform.OS === 'android') {
      DateTimePickerAndroid.open({
        value: pickerValue,
        mode: 'time',
        display: 'spinner',
        is24Hour: true,
        onChange: (event, selectedDate) => {
          if (event.type === 'set' && selectedDate) {
            const h = String(selectedDate.getHours()).padStart(2, '0');
            const m = String(selectedDate.getMinutes()).padStart(2, '0');
            setForm((prev) => ({ ...prev, endTime: `${h}:${m}` }));
          }
        },
      });
      return;
    }

    draftEndTimeRef.current = pickerValue;
    setDraftEndTime(pickerValue);
    setShowEndTimeModal(true);
  };

  const saveStartTimeFromModal = () => {
    const finalDate = draftStartTimeRef.current;
    const h = String(finalDate.getHours()).padStart(2, '0');
    const m = String(finalDate.getMinutes()).padStart(2, '0');
    setForm((prev) => ({ ...prev, startTime: `${h}:${m}` }));
    setShowStartTimeModal(false);
  };

  const saveEndTimeFromModal = () => {
    const finalDate = draftEndTimeRef.current;
    const h = String(finalDate.getHours()).padStart(2, '0');
    const m = String(finalDate.getMinutes()).padStart(2, '0');
    setForm((prev) => ({ ...prev, endTime: `${h}:${m}` }));
    setShowEndTimeModal(false);
  };

  const createInvitation = async () => {
    resetStatusMessages();
    const recipients = selectedRecipientItems.map((item) => ({
      parentId: Number(item.parentId),
      childId: Number(item.childId),
    }));

    if (!instructorId) {
      setErrorText('לא זוהה מדריך מחובר.');
      return;
    }

    if (!form.meetingDate || !form.startTime || !form.endTime) {
      setErrorText('יש למלא תאריך, שעת התחלה ושעת סיום.');
      return;
    }

    if (!String(form.targetMetric || '').trim()) {
      setErrorText('יש לבחור מדד מטרה לשיעור.');
      return;
    }

    if (!/^\d{4}-\d{2}-\d{2}$/.test(form.meetingDate.trim())) {
      setErrorText('פורמט התאריך חייב להיות yyyy-MM-dd.');
      return;
    }

    const israelTodayIsoDate = getIsraelTodayIsoDate();
    if (israelTodayIsoDate && form.meetingDate.trim() < israelTodayIsoDate) {
      setErrorText('לא ניתן לקבוע שיעור בתאריך עבר.');
      return;
    }

    const startTimeMinutes = parseHourMinuteToMinutes(form.startTime);
    const endTimeMinutes = parseHourMinuteToMinutes(form.endTime);

    if (startTimeMinutes === null || endTimeMinutes === null) {
      setErrorText('פורמט השעה חייב להיות HH:mm.');
      return;
    }

    if (startTimeMinutes >= endTimeMinutes) {
      setErrorText('שעת התחלה חייבת להיות מוקדמת משעת הסיום.');
      return;
    }

    const meetingStartMinuteStamp = buildComparableMinuteStamp(form.meetingDate, startTimeMinutes);
    if (meetingStartMinuteStamp === null) {
      setErrorText('תאריך או שעת התחלה אינם תקינים.');
      return;
    }

    const israelNowMinuteStamp = getIsraelNowMinuteStamp();
    if (israelNowMinuteStamp === null) {
      setErrorText('לא ניתן לאמת שעה נוכחית לפי שעון ישראל. נסה שוב.');
      return;
    }

    const earliestAllowedStartMinuteStamp = israelNowMinuteStamp + 60;

    if (meetingStartMinuteStamp < earliestAllowedStartMinuteStamp) {
      setErrorText('לא ניתן ליצור שיעור שמתחיל בפחות מ-60 דקות מעכשיו.');
      return;
    }

    if (recipients.length === 0) {
      setErrorText('יש לבחור לפחות הורה-ילד אחד.');
      return;
    }

    if (form.mode === 'private' && recipients.length !== 1) {
      setErrorText('בשיעור פרטי יש לבחור ילד אחד בלבד.');
      return;
    }

    if (form.mode === 'group' && !Number(form.groupId || 0)) {
      setErrorText('בשיעור קבוצתי יש לבחור קבוצה.');
      return;
    }

    const capacity = form.mode === 'private' ? 1 : Number(form.capacity || 0);
    const minRegistrations = form.mode === 'private' ? 1 : Number(form.minRegistrations || 0);

    if (form.mode === 'group') {
      if (!form.capacity.trim() || !Number.isInteger(capacity) || capacity < 2 || capacity > 20) {
        setErrorText('מקסימום ילדים בשיעור קבוצתי חייב להיות מספר שלם בין 2 ל-20.');
        return;
      }

      if (!form.minRegistrations.trim() || !Number.isInteger(minRegistrations) || minRegistrations < 2 || minRegistrations > 20) {
        setErrorText('מינימום ילדים בשיעור קבוצתי חייב להיות מספר שלם בין 2 ל-20.');
        return;
      }

      if (minRegistrations > capacity) {
        setErrorText('מינימום ילדים לא יכול להיות גדול ממקסימום הילדים.');
        return;
      }
    }

    const payload = {
      lessonType: form.mode === 'private' ? 'Private' : 'Group',
      meetingDate: form.meetingDate.trim(),
      startTime: form.startTime.trim(),
      endTime: form.endTime.trim(),
      groupId: form.mode === 'group' ? Number(form.groupId || 0) : null,
      capacity,
      minRegistrations,
      generalNote: form.generalNote.trim(),
      targetMetric: form.targetMetric ? form.targetMetric.trim() : '',
      recipients,
    };

    try {
      setIsSubmitting(true);
      const response = await fetch(`${API_BASE_URL}/lesson-scheduling/instructor/${instructorId}/invitations`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      const body = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(body?.message || 'יצירת ההזמנה נכשלה.');
      }

      setSuccessText('הזמנה נשלחה בהצלחה, נרשמה בתיבת ההתראות ונשלחה גם כהתראת פוש למשתמשים מחוברים.');
      setForm((prev) => ({
        ...defaultFormState,
        mode: prev.mode,
        capacity: prev.mode === 'private' ? '1' : '20',
        minRegistrations: prev.mode === 'private' ? '1' : '2',
      }));
      setSelectedRecipients({});
      setShowGroupDropdown(false);
    } catch (error) {
      setErrorText(error?.message || 'יצירת ההזמנה נכשלה.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar translucent backgroundColor="transparent" barStyle="light-content" />
      <AquaticBackground variant="instructor" showWaves={false} />

      <RoleHeader
        title="קביעת שיעור"
        onMenuPress={() => navigation.goBack()}
        leftIcon="›"
        rightIcon="↻"
        rightLabel="רענון"
        onRightPress={refreshAll}
        theme="dark"
      />

      <ScrollView
        contentContainerStyle={styles.content}
        showsVerticalScrollIndicator={false}
        scrollEnabled={!showGroupDropdown && !showMetricDropdown && !recipientDropdownOpen}
      >
        <View style={styles.panel}>
          <Text style={styles.panelTitle}>סוג הזמנה</Text>
          <View style={styles.modeRow}>
            <TouchableOpacity
              activeOpacity={0.85}
              style={[styles.modeChip, form.mode === 'private' && styles.modeChipActive]}
              onPress={() => onModeChange('private')}
            >
              <Text style={[styles.modeChipText, form.mode === 'private' && styles.modeChipTextActive]}>פרטי</Text>
            </TouchableOpacity>
            <TouchableOpacity
              activeOpacity={0.85}
              style={[styles.modeChip, form.mode === 'group' && styles.modeChipActive]}
              onPress={() => onModeChange('group')}
            >
              <Text style={[styles.modeChipText, form.mode === 'group' && styles.modeChipTextActive]}>קבוצתי</Text>
            </TouchableOpacity>
          </View>

          <View style={styles.row}>
            <TouchableOpacity
              activeOpacity={0.82}
              style={[styles.input, styles.halfInput, styles.dateField]}
              onPress={openMeetingDatePicker}
            >
              <Text style={[styles.dateFieldText, !form.meetingDate && styles.placeholderText]}>
                {form.meetingDate ? formatDisplayDate(form.meetingDate) : 'בחר תאריך'}
              </Text>
            </TouchableOpacity>
            <TouchableOpacity
              activeOpacity={0.82}
              style={[styles.input, styles.halfInput, styles.dateField]}
              onPress={openStartTimePicker}
            >
              <Text style={[styles.dateFieldText, !form.startTime && styles.placeholderText]}>
                {form.startTime ? form.startTime : 'התחלה'}
              </Text>
            </TouchableOpacity>
          </View>

          {form.mode === 'group' ? (
            <>
              <View style={styles.row}>
                <TouchableOpacity
                  activeOpacity={0.82}
                  style={[styles.input, styles.halfInput, styles.dateField]}
                  onPress={openEndTimePicker}
                >
                  <Text style={[styles.dateFieldText, !form.endTime && styles.placeholderText]}>
                    {form.endTime ? form.endTime : 'סיום'}
                  </Text>
                </TouchableOpacity>
                <TouchableOpacity
                  activeOpacity={0.82}
                  style={[styles.input, styles.halfInput, styles.dateField]}
                  onPress={() => {
                    setShowGroupDropdown(false);
                    setShowMetricDropdown(false);
                    setRecipientDropdownOpen(false);
                    setShowCapacityModal(true);
                  }}
                >
                  <Text style={[styles.dateFieldText, !form.capacity && styles.placeholderText]}>
                    {form.capacity ? `מקסימום ילדים: ${form.capacity}` : 'מקסימום ילדים'}
                  </Text>
                </TouchableOpacity>
              </View>

              <View style={styles.row}>
                <TouchableOpacity
                  activeOpacity={0.82}
                  style={[styles.input, styles.halfInput, styles.dateField]}
                  onPress={() => {
                    setShowGroupDropdown(false);
                    setShowMetricDropdown(false);
                    setRecipientDropdownOpen(false);
                    setShowMinRegistrationsModal(true);
                  }}
                >
                  <Text style={[styles.dateFieldText, !form.minRegistrations && styles.placeholderText]}>
                    {form.minRegistrations ? `מינימום ילדים: ${form.minRegistrations}` : 'מינימום ילדים'}
                  </Text>
                </TouchableOpacity>

                <View style={styles.halfInput}>
                  <TouchableOpacity
                    activeOpacity={0.82}
                    style={[styles.input, styles.groupDropdownTrigger]}
                    onPress={() => {
                      setShowMetricDropdown(false);
                      setRecipientDropdownOpen(false);
                      setShowGroupDropdown((prev) => !prev);
                    }}
                  >
                    <Text style={[styles.groupDropdownValue, !selectedGroup && styles.placeholderText]}>
                      {selectedGroup ? selectedGroup.name : 'בחר קבוצה'}
                    </Text>
                  </TouchableOpacity>

                  {showGroupDropdown ? (
                    <ScrollView style={styles.groupDropdownMenu} nestedScrollEnabled={true}>
                      {isLoadingGroups ? (
                        <Text style={styles.groupDropdownState}>טוען קבוצות...</Text>
                      ) : null}

                      {!isLoadingGroups && instructorGroups.length === 0 ? (
                        <Text style={styles.groupDropdownState}>אין קבוצות פעילות זמינות.</Text>
                      ) : null}

                      {!isLoadingGroups && instructorGroups.map((group) => (
                        <TouchableOpacity
                          key={String(group.groupId)}
                          activeOpacity={0.82}
                          style={styles.groupOption}
                          onPress={() => {
                            setForm((prev) => ({ ...prev, groupId: String(group.groupId) }));
                            setShowGroupDropdown(false);
                          }}
                        >
                          <Text style={styles.groupOptionText}>{group.name || `קבוצה ${group.groupId}`}</Text>
                        </TouchableOpacity>
                      ))}
                    </ScrollView>
                  ) : null}
                </View>
              </View>
            </>
          ) : (
            <>
              <TouchableOpacity
                activeOpacity={0.82}
                style={[styles.input, styles.dateField]}
                onPress={openEndTimePicker}
              >
                <Text style={[styles.dateFieldText, !form.endTime && styles.placeholderText]}>
                  {form.endTime ? form.endTime : 'סיום'}
                </Text>
              </TouchableOpacity>
              <View style={styles.privateFixedInfo}>
                <Text style={styles.privateFixedInfoText}>בשיעור פרטי הקיבולת ומינימום הנרשמים נקבעים אוטומטית ל-1.</Text>
              </View>
            </>
          )}

          {/* Target Metric Dropdown */}
          <View style={{ zIndex: 10 }}>
            <TouchableOpacity
              activeOpacity={0.82}
              style={[styles.input, styles.groupDropdownTrigger]}
              onPress={() => {
                setShowGroupDropdown(false);
                setRecipientDropdownOpen(false);
                setShowMetricDropdown((prev) => !prev);
              }}
            >
              <Text style={[styles.groupDropdownValue, !form.targetMetric && styles.placeholderText]}>
                {form.targetMetric ? `מדד מטרה: ${form.targetMetric}` : 'בחרו מדד מטרה (חובה)'}
              </Text>
            </TouchableOpacity>

            {showMetricDropdown ? (
              <ScrollView style={styles.groupDropdownMenu} nestedScrollEnabled={true}>
                {METRIC_OPTIONS.map((opt) => (
                  <TouchableOpacity
                    key={opt.value}
                    activeOpacity={0.82}
                    style={styles.groupOption}
                    onPress={() => {
                      setForm((prev) => ({ ...prev, targetMetric: opt.value }));
                      setShowMetricDropdown(false);
                    }}
                  >
                    <Text style={styles.groupOptionText}>{opt.label}</Text>
                  </TouchableOpacity>
                ))}
              </ScrollView>
            ) : null}
          </View>

          <TextInput
            value={form.generalNote}
            onChangeText={(text) => setForm((prev) => ({ ...prev, generalNote: text }))}
            placeholder="הערה להורים"
            placeholderTextColor="#86A2B7"
            style={[styles.input, styles.multilineInput]}
            multiline
            textAlignVertical="top"
          />
        </View>

        <View style={styles.panel}>
          <Text style={styles.panelTitle}>נמענים להזמנה</Text>
          <Text style={styles.helperText}>
            בחרו הורה (שם מלא + מייל) ואת הילד/ה שלו/ה, והוסיפו כל נמען/ת לרשימה.
          </Text>

          {isLoadingPool ? <Text style={styles.stateText}>טוען נמענים...</Text> : null}

          {!isLoadingPool && recipientPool.length === 0 ? (
            <Text style={styles.stateText}>אין נמענים זמינים כרגע.</Text>
          ) : null}

          <View style={styles.recipientPickerRow}>
            <View style={styles.recipientPickerFieldWrap}>
              <TouchableOpacity
                activeOpacity={0.82}
                style={[styles.input, styles.recipientDropdownTrigger]}
                onPress={() => {
                  if (form.mode === 'group' && !form.groupId) {
                    setErrorText('יש לבחור קבוצה תחילה.');
                    return;
                  }
                  setErrorText('');
                  setShowGroupDropdown(false);
                  setShowMetricDropdown(false);
                  setRecipientDropdownOpen((prev) => !prev);
                }}
              >
                <Text style={[styles.groupDropdownValue, !draftRecipientOption && styles.placeholderText]}>
                  {draftRecipientOption
                    ? `${draftRecipientOption.parentFullName} (${draftRecipientOption.parentEmail || 'ללא מייל'}) • ${draftRecipientOption.childFullName}`
                    : (form.mode === 'group' && !form.groupId ? 'נא לבחור קבוצה תחילה' : 'בחר הורה וילד')}
                </Text>
              </TouchableOpacity>

              {recipientDropdownOpen ? (
                <ScrollView style={styles.recipientDropdownMenu} nestedScrollEnabled>
                  {visibleRecipientOptions.map((item) => {
                    const key = recipientKey(item.parentId, item.childId);
                    const isChosen = !!selectedRecipients[key];

                    return (
                      <TouchableOpacity
                        key={key}
                        activeOpacity={0.82}
                        style={styles.recipientOption}
                        onPress={() => setDraftRecipientKey(key)}
                      >
                        <Text style={styles.recipientOptionTitle}>{item.parentFullName} ({item.parentEmail || 'ללא מייל'})</Text>
                        <Text style={styles.recipientOptionSubtitle}>
                          ילד/ה: {item.childFullName}{isChosen ? ' • כבר ברשימה' : ''}
                        </Text>
                      </TouchableOpacity>
                    );
                  })}
                </ScrollView>
              ) : null}
            </View>

            <TouchableOpacity
              activeOpacity={0.82}
              style={styles.addRecipientButton}
              onPress={addRecipientFromDropdown}
            >
              <Text style={styles.addRecipientButtonText}>{form.mode === 'private' ? 'בחירה' : 'הוספה'}</Text>
            </TouchableOpacity>
          </View>

          <View style={styles.selectedRecipientsBox}>
            <Text style={styles.selectedRecipientsTitle}>נמענים שנבחרו</Text>

            {selectedRecipientItems.length === 0 ? (
              <Text style={styles.stateText}>עדיין לא נוספו נמענים.</Text>
            ) : null}

            {selectedRecipientItems.map((item) => (
              <View key={recipientKey(item.parentId, item.childId)} style={styles.selectedRecipientRow}>
                <TouchableOpacity
                  activeOpacity={0.82}
                  style={styles.removeRecipientButton}
                  onPress={() => removeSelectedRecipient(item.parentId, item.childId)}
                >
                  <Text style={styles.removeRecipientButtonText}>הסר</Text>
                </TouchableOpacity>

                <View style={styles.selectedRecipientTextWrap}>
                  <Text style={styles.selectedRecipientTitle}>{item.parentFullName} ({item.parentEmail || 'ללא מייל'})</Text>
                  <Text style={styles.selectedRecipientSubtitle}>ילד/ה: {item.childFullName}</Text>
                </View>
              </View>
            ))}
          </View>
        </View>

        {errorText ? <Text style={styles.errorText}>{errorText}</Text> : null}
        {successText ? <Text style={styles.successText}>{successText}</Text> : null}

        <PrimaryButton
          label={isSubmitting ? 'שולח הזמנה...' : 'שליחת הזמנה'}
          onPress={createInvitation}
          disabled={isSubmitting}
          style={styles.buttonShell}
          gradientStyle={styles.submitButton}
          textStyle={styles.buttonText}
          colorsOverride={['#2f93dd', '#1c6cb2']}
        />

        {Platform.OS !== 'android' && showDateModal ? (
          <Modal
            visible={showDateModal}
            transparent
            animationType="fade"
            onRequestClose={() => setShowDateModal(false)}
          >
            <View style={styles.dateModalOverlay}>
              <View style={styles.dateModalCard}>
                <Text style={styles.dateModalTitle}>בחירת תאריך לשיעור</Text>

                <DateTimePicker
                  value={draftMeetingDate}
                  mode="date"
                  display={Platform.OS === 'ios' ? 'spinner' : 'default'}
                  textColor="#001529"
                  accentColor="#0082b0"
                  locale="he-IL"
                  minimumDate={parseIsoDate(getIsraelTodayIsoDate()) || new Date()}
                  maximumDate={new Date(2100, 11, 31)}
                  onChange={(_event, selectedDate) => {
                    if (selectedDate) {
                      setDraftMeetingDate(selectedDate);
                      draftMeetingDateRef.current = selectedDate;
                    }
                  }}
                  style={styles.datePickerControl}
                />

                <View style={styles.dateModalActions}>
                  <TouchableOpacity
                    activeOpacity={0.82}
                    style={styles.dateModalButtonSecondary}
                    onPress={() => setShowDateModal(false)}
                  >
                    <Text style={styles.dateModalButtonSecondaryText}>ביטול</Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    activeOpacity={0.82}
                    style={styles.dateModalButtonPrimary}
                    onPress={saveMeetingDateFromModal}
                  >
                    <Text style={styles.dateModalButtonPrimaryText}>שמירה</Text>
                  </TouchableOpacity>
                </View>
              </View>
            </View>
          </Modal>
        ) : null}

        {showStartTimeModal ? (
          <Modal
            visible={showStartTimeModal}
            transparent
            animationType="fade"
            onRequestClose={() => setShowStartTimeModal(false)}
          >
            <View style={styles.dateModalOverlay}>
              <View style={styles.dateModalCard}>
                <Text style={styles.dateModalTitle}>בחירת שעת התחלה</Text>

                <View style={styles.timePickerRow}>
                  <View style={styles.timePickerColumn}>
                    <Text style={styles.timePickerLabel}>דקות</Text>
                    <ScrollView style={styles.timePickerScroll} nestedScrollEnabled>
                      {Array.from({ length: 60 }, (_, i) => i).map((m) => {
                        const now = new Date();
                        const isToday = form.meetingDate === getIsraelTodayIsoDate();
                        const selectedHour = draftStartTime.getHours();
                        const isDisabled = isToday && selectedHour === now.getHours() && m <= now.getMinutes();
                        const currentMin = draftStartTime.getMinutes();
                        const isSelected = currentMin === m;
                        return (
                          <TouchableOpacity
                            key={m}
                            activeOpacity={0.8}
                            disabled={isDisabled}
                            style={[styles.timePickerItem, isSelected && styles.timePickerItemActive, isDisabled && styles.timePickerItemDisabled]}
                            onPress={() => {
                              const d = new Date(draftStartTime);
                              d.setMinutes(m);
                              setDraftStartTime(d);
                              draftStartTimeRef.current = d;
                            }}
                          >
                            <Text style={[styles.timePickerItemText, isSelected && styles.timePickerItemTextActive, isDisabled && styles.timePickerItemTextDisabled]}>
                              {String(m).padStart(2, '0')}
                            </Text>
                          </TouchableOpacity>
                        );
                      })}
                    </ScrollView>
                  </View>

                  <View style={styles.timePickerColumn}>
                    <Text style={styles.timePickerLabel}>שעה</Text>
                    <ScrollView style={styles.timePickerScroll} nestedScrollEnabled>
                      {Array.from({ length: 24 }, (_, i) => i).map((h) => {
                        const now = new Date();
                        const isToday = form.meetingDate === getIsraelTodayIsoDate();
                        const isDisabled = isToday && h < now.getHours();
                        const currentHour = draftStartTime.getHours();
                        const isSelected = currentHour === h;
                        return (
                          <TouchableOpacity
                            key={h}
                            activeOpacity={0.8}
                            disabled={isDisabled}
                            style={[styles.timePickerItem, isSelected && styles.timePickerItemActive, isDisabled && styles.timePickerItemDisabled]}
                            onPress={() => {
                              const d = new Date(draftStartTime);
                              d.setHours(h);
                              setDraftStartTime(d);
                              draftStartTimeRef.current = d;
                            }}
                          >
                            <Text style={[styles.timePickerItemText, isSelected && styles.timePickerItemTextActive, isDisabled && styles.timePickerItemTextDisabled]}>
                              {String(h).padStart(2, '0')}
                            </Text>
                          </TouchableOpacity>
                        );
                      })}
                    </ScrollView>
                  </View>
                </View>

                <View style={styles.dateModalActions}>
                  <TouchableOpacity
                    activeOpacity={0.82}
                    style={styles.dateModalButtonSecondary}
                    onPress={() => setShowStartTimeModal(false)}
                  >
                    <Text style={styles.dateModalButtonSecondaryText}>ביטול</Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    activeOpacity={0.82}
                    style={styles.dateModalButtonPrimary}
                    onPress={saveStartTimeFromModal}
                  >
                    <Text style={styles.dateModalButtonPrimaryText}>שמירה</Text>
                  </TouchableOpacity>
                </View>
              </View>
            </View>
          </Modal>
        ) : null}

        {showEndTimeModal ? (
          <Modal
            visible={showEndTimeModal}
            transparent
            animationType="fade"
            onRequestClose={() => setShowEndTimeModal(false)}
          >
            <View style={styles.dateModalOverlay}>
              <View style={styles.dateModalCard}>
                <Text style={styles.dateModalTitle}>בחירת שעת סיום</Text>

                <View style={styles.timePickerRow}>
                  <View style={styles.timePickerColumn}>
                    <Text style={styles.timePickerLabel}>דקות</Text>
                    <ScrollView style={styles.timePickerScroll} nestedScrollEnabled>
                      {Array.from({ length: 60 }, (_, i) => i).map((m) => {
                        const startH = form.startTime ? Number(form.startTime.split(':')[0]) : 0;
                        const startM = form.startTime ? Number(form.startTime.split(':')[1]) : 0;
                        const selectedHour = draftEndTime.getHours();
                        const isDisabled = selectedHour === startH && m <= startM;
                        const currentMin = draftEndTime.getMinutes();
                        const isSelected = currentMin === m;
                        return (
                          <TouchableOpacity
                            key={m}
                            activeOpacity={0.8}
                            disabled={isDisabled}
                            style={[styles.timePickerItem, isSelected && styles.timePickerItemActive, isDisabled && styles.timePickerItemDisabled]}
                            onPress={() => {
                              const d = new Date(draftEndTime);
                              d.setMinutes(m);
                              setDraftEndTime(d);
                              draftEndTimeRef.current = d;
                            }}
                          >
                            <Text style={[styles.timePickerItemText, isSelected && styles.timePickerItemTextActive, isDisabled && styles.timePickerItemTextDisabled]}>
                              {String(m).padStart(2, '0')}
                            </Text>
                          </TouchableOpacity>
                        );
                      })}
                    </ScrollView>
                  </View>

                  <View style={styles.timePickerColumn}>
                    <Text style={styles.timePickerLabel}>שעה</Text>
                    <ScrollView style={styles.timePickerScroll} nestedScrollEnabled>
                      {Array.from({ length: 24 }, (_, i) => i).map((h) => {
                        const startH = form.startTime ? Number(form.startTime.split(':')[0]) : 0;
                        const isDisabled = h < startH;
                        const currentHour = draftEndTime.getHours();
                        const isSelected = currentHour === h;
                        return (
                          <TouchableOpacity
                            key={h}
                            activeOpacity={0.8}
                            disabled={isDisabled}
                            style={[styles.timePickerItem, isSelected && styles.timePickerItemActive, isDisabled && styles.timePickerItemDisabled]}
                            onPress={() => {
                              const d = new Date(draftEndTime);
                              d.setHours(h);
                              setDraftEndTime(d);
                              draftEndTimeRef.current = d;
                            }}
                          >
                            <Text style={[styles.timePickerItemText, isSelected && styles.timePickerItemTextActive, isDisabled && styles.timePickerItemTextDisabled]}>
                              {String(h).padStart(2, '0')}
                            </Text>
                          </TouchableOpacity>
                        );
                      })}
                    </ScrollView>
                  </View>
                </View>

                <View style={styles.dateModalActions}>
                  <TouchableOpacity
                    activeOpacity={0.82}
                    style={styles.dateModalButtonSecondary}
                    onPress={() => setShowEndTimeModal(false)}
                  >
                    <Text style={styles.dateModalButtonSecondaryText}>ביטול</Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    activeOpacity={0.82}
                    style={styles.dateModalButtonPrimary}
                    onPress={saveEndTimeFromModal}
                  >
                    <Text style={styles.dateModalButtonPrimaryText}>שמירה</Text>
                  </TouchableOpacity>
                </View>
              </View>
            </View>
          </Modal>
        ) : null}

        <Modal
          visible={showCapacityModal}
          transparent
          animationType="fade"
          onRequestClose={() => setShowCapacityModal(false)}
        >
          <View style={styles.dateModalOverlay}>
            <View style={styles.dateModalCard}>
              <Text style={styles.dateModalTitle}>בחירת מקסימום ילדים</Text>
              
              <View style={styles.numberGrid}>
                {NUMBER_OPTIONS.map((num) => {
                  const isSelected = Number(form.capacity) === num;
                  return (
                    <TouchableOpacity
                      key={num}
                      activeOpacity={0.8}
                      style={[
                        styles.numberGridItem,
                        isSelected && styles.numberGridItemActive
                      ]}
                      onPress={() => {
                        setForm((prev) => {
                          const nextMin = Number(prev.minRegistrations) > num ? String(num) : prev.minRegistrations;
                          return {
                            ...prev,
                            capacity: String(num),
                            minRegistrations: nextMin,
                          };
                        });
                        setShowCapacityModal(false);
                      }}
                    >
                      <Text
                        style={[
                          styles.numberGridItemText,
                          isSelected && styles.numberGridItemTextActive
                        ]}
                      >
                        {num}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>

              <View style={styles.dateModalActions}>
                <TouchableOpacity
                  activeOpacity={0.82}
                  style={styles.dateModalButtonSecondary}
                  onPress={() => setShowCapacityModal(false)}
                >
                  <Text style={styles.dateModalButtonSecondaryText}>ביטול</Text>
                </TouchableOpacity>
              </View>
            </View>
          </View>
        </Modal>

        <Modal
          visible={showMinRegistrationsModal}
          transparent
          animationType="fade"
          onRequestClose={() => setShowMinRegistrationsModal(false)}
        >
          <View style={styles.dateModalOverlay}>
            <View style={styles.dateModalCard}>
              <Text style={styles.dateModalTitle}>בחירת מינימום ילדים</Text>
              
              <View style={styles.numberGrid}>
                {NUMBER_OPTIONS.map((num) => {
                  const isSelected = Number(form.minRegistrations) === num;
                  const isDisabled = num > Number(form.capacity);
                  return (
                    <TouchableOpacity
                      key={num}
                      disabled={isDisabled}
                      activeOpacity={0.8}
                      style={[
                        styles.numberGridItem,
                        isSelected && styles.numberGridItemActive,
                        isDisabled && styles.numberGridItemDisabled
                      ]}
                      onPress={() => {
                        setForm((prev) => ({ ...prev, minRegistrations: String(num) }));
                        setShowMinRegistrationsModal(false);
                      }}
                    >
                      <Text
                        style={[
                          styles.numberGridItemText,
                          isSelected && styles.numberGridItemTextActive,
                          isDisabled && styles.numberGridItemTextDisabled
                        ]}
                      >
                        {num}
                      </Text>
                    </TouchableOpacity>
                  );
                })}
              </View>

              <View style={styles.dateModalActions}>
                <TouchableOpacity
                  activeOpacity={0.82}
                  style={styles.dateModalButtonSecondary}
                  onPress={() => setShowMinRegistrationsModal(false)}
                >
                  <Text style={styles.dateModalButtonSecondaryText}>ביטול</Text>
                </TouchableOpacity>
              </View>
            </View>
          </View>
        </Modal>

      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: '#001529', paddingTop: Platform.OS === 'android' ? StatusBar.currentHeight : 0 },
  content: { paddingHorizontal: 16, paddingTop: 8, paddingBottom: 48, gap: 12 },
  panel: {
    backgroundColor: 'rgba(7, 60, 98, 0.68)',
    borderRadius: 16,
    padding: 16,
    borderWidth: 1,
    borderColor: 'rgba(130, 219, 255, 0.3)',
    gap: 8,
  },
  panelTitle: {
    width: '100%',
    color: '#e7f8ff',
    fontSize: 16,
    fontWeight: '800',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  helperText: {
    width: '100%',
    color: 'rgba(223, 242, 255, 0.86)',
    fontSize: 12,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  modeRow: {
    flexDirection: 'row',
    gap: 10,
  },
  modeChip: {
    paddingVertical: 9,
    paddingHorizontal: 14,
    borderRadius: 999,
    borderWidth: 1,
    borderColor: 'rgba(128, 206, 248, 0.5)',
    backgroundColor: 'rgba(15, 92, 142, 0.58)',
  },
  modeChipActive: {
    backgroundColor: '#1b8bce',
    borderColor: '#71d8ff',
  },
  modeChipText: {
    color: '#d8f3ff',
    fontWeight: '700',
    fontSize: 13,
  },
  modeChipTextActive: {
    color: '#FFFFFF',
  },
  row: {
    flexDirection: 'row',
    gap: 10,
  },
  input: {
    borderWidth: 1,
    borderColor: 'rgba(136, 214, 255, 0.34)',
    borderRadius: 10,
    paddingHorizontal: 10,
    paddingVertical: 10,
    fontSize: 14,
    color: '#e6f8ff',
    backgroundColor: 'rgba(13, 89, 140, 0.56)',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  dateField: {
    justifyContent: 'center',
  },
  dateFieldText: {
    width: '100%',
    fontSize: 14,
    color: '#e6f8ff',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  placeholderText: {
    color: 'rgba(193, 229, 250, 0.62)',
  },
  groupDropdownTrigger: {
    justifyContent: 'center',
  },
  groupDropdownValue: {
    width: '100%',
    fontSize: 14,
    color: '#e6f8ff',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  groupDropdownMenu: {
    marginTop: 6,
    borderWidth: 1,
    borderColor: 'rgba(136, 214, 255, 0.34)',
    borderRadius: 10,
    backgroundColor: 'rgba(5, 66, 107, 0.96)',
    maxHeight: 180,
    overflow: 'hidden',
  },
  groupDropdownState: {
    paddingHorizontal: 10,
    paddingVertical: 10,
    color: 'rgba(210, 237, 255, 0.88)',
    fontSize: 13,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  groupOption: {
    paddingHorizontal: 10,
    paddingVertical: 10,
    borderTopWidth: 1,
    borderTopColor: 'rgba(147, 219, 255, 0.22)',
  },
  groupOptionText: {
    color: '#e6f8ff',
    fontSize: 14,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  privateFixedInfo: {
    borderWidth: 1,
    borderColor: 'rgba(136, 214, 255, 0.34)',
    backgroundColor: 'rgba(12, 87, 136, 0.45)',
    borderRadius: 10,
    paddingHorizontal: 10,
    paddingVertical: 9,
  },
  privateFixedInfoText: {
    color: 'rgba(216, 240, 255, 0.88)',
    fontSize: 12,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  halfInput: {
    flex: 1,
  },
  multilineInput: {
    minHeight: 74,
  },
  recipientPickerRow: {
    flexDirection: 'row',
    gap: 8,
    alignItems: 'flex-start',
  },
  recipientPickerFieldWrap: {
    flex: 1,
  },
  recipientDropdownTrigger: {
    justifyContent: 'center',
  },
  recipientDropdownMenu: {
    marginTop: 6,
    borderWidth: 1,
    borderColor: 'rgba(136, 214, 255, 0.34)',
    borderRadius: 10,
    backgroundColor: 'rgba(5, 66, 107, 0.96)',
    maxHeight: 220,
  },
  recipientOption: {
    paddingHorizontal: 10,
    paddingVertical: 10,
    borderTopWidth: 1,
    borderTopColor: 'rgba(147, 219, 255, 0.22)',
  },
  recipientOptionTitle: {
    color: '#e4f8ff',
    fontSize: 13,
    fontWeight: '700',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  recipientOptionSubtitle: {
    marginTop: 3,
    color: 'rgba(205, 235, 255, 0.84)',
    fontSize: 12,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  addRecipientButton: {
    minWidth: 72,
    borderRadius: 10,
    backgroundColor: '#1d79be',
    paddingHorizontal: 12,
    paddingVertical: 11,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 1,
    borderColor: 'rgba(158, 227, 255, 0.45)',
  },
  addRecipientButtonText: {
    color: '#FFFFFF',
    fontSize: 13,
    fontWeight: '700',
  },
  selectedRecipientsBox: {
    marginTop: 8,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: 'rgba(136, 214, 255, 0.34)',
    backgroundColor: 'rgba(13, 87, 136, 0.44)',
    padding: 10,
    gap: 8,
  },
  selectedRecipientsTitle: {
    width: '100%',
    color: '#e9f9ff',
    fontSize: 13,
    fontWeight: '800',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  selectedRecipientRow: {
    flexDirection: 'row-reverse',
    alignItems: 'center',
    gap: 8,
    borderTopWidth: 1,
    borderTopColor: 'rgba(150, 221, 255, 0.22)',
    paddingTop: 8,
  },
  selectedRecipientTextWrap: {
    flex: 1,
  },
  selectedRecipientTitle: {
    width: '100%',
    color: '#e6f8ff',
    fontSize: 13,
    fontWeight: '700',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  selectedRecipientSubtitle: {
    width: '100%',
    marginTop: 2,
    color: 'rgba(214, 240, 255, 0.86)',
    fontSize: 12,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  removeRecipientButton: {
    borderRadius: 8,
    backgroundColor: 'rgba(194, 74, 90, 0.22)',
    borderWidth: 1,
    borderColor: 'rgba(255, 160, 174, 0.38)',
    paddingHorizontal: 10,
    paddingVertical: 6,
  },
  removeRecipientButtonText: {
    color: '#ffdbe1',
    fontSize: 12,
    fontWeight: '700',
  },
  stateText: {
    width: '100%',
    color: 'rgba(220, 241, 255, 0.86)',
    fontSize: 13,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  errorText: {
    width: '100%',
    color: '#ffd0d8',
    fontSize: 14,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  successText: {
    width: '100%',
    color: '#abffe0',
    fontSize: 14,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  buttonShell: { marginTop: 2 },
  submitButton: { paddingVertical: 14 },
  buttonText: { color: '#FFFFFF', fontSize: 16, fontWeight: '700' },
  dateModalOverlay: {
    flex: 1,
    backgroundColor: 'rgba(1, 20, 35, 0.62)',
    justifyContent: 'center',
    paddingHorizontal: 24,
  },
  dateModalCard: {
    backgroundColor: 'rgba(6, 62, 102, 0.95)',
    borderRadius: 16,
    padding: 16,
    gap: 12,
    borderWidth: 1,
    borderColor: 'rgba(140, 220, 255, 0.38)',
  },
  dateModalTitle: {
    width: '100%',
    color: '#e8f8ff',
    fontSize: 16,
    fontWeight: '800',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  datePickerControl: {
    backgroundColor: '#ffffff',
    borderRadius: 12,
    height: 180,
    width: 260,
    alignSelf: 'center',
    justifyContent: 'center',
  },
  dateModalActions: {
    flexDirection: 'row',
    gap: 10,
  },
  dateModalButtonPrimary: {
    flex: 1,
    borderRadius: 10,
    backgroundColor: '#1d79be',
    paddingVertical: 10,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: 'rgba(158, 227, 255, 0.45)',
  },
  dateModalButtonPrimaryText: {
    color: '#FFFFFF',
    fontSize: 14,
    fontWeight: '700',
  },
  dateModalButtonSecondary: {
    flex: 1,
    borderRadius: 10,
    backgroundColor: 'rgba(10, 82, 129, 0.7)',
    paddingVertical: 10,
    alignItems: 'center',
    borderWidth: 1,
    borderColor: 'rgba(140, 220, 255, 0.35)',
  },
  dateModalButtonSecondaryText: {
    color: '#e2f7ff',
    fontSize: 14,
    fontWeight: '700',
  },
  numberGrid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    justifyContent: 'center',
    gap: 8,
    marginVertical: 12,
  },
  numberGridItem: {
    width: 42,
    height: 42,
    borderRadius: 21,
    borderWidth: 1,
    borderColor: 'rgba(136, 214, 255, 0.3)',
    backgroundColor: 'rgba(13, 89, 140, 0.56)',
    alignItems: 'center',
    justifyContent: 'center',
  },
  numberGridItemActive: {
    backgroundColor: '#1b8bce',
    borderColor: '#71d8ff',
  },
  numberGridItemText: {
    color: '#e6f8ff',
    fontSize: 14,
    fontWeight: '600',
  },
  numberGridItemTextActive: {
    color: '#ffffff',
    fontWeight: '800',
  },
  numberGridItemDisabled: {
    backgroundColor: 'rgba(10, 30, 50, 0.3)',
    borderColor: 'rgba(136, 214, 255, 0.1)',
  },
  numberGridItemTextDisabled: {
    color: 'rgba(230, 248, 255, 0.25)',
  },
  timePickerRow: {
    flexDirection: 'row',
    gap: 12,
    justifyContent: 'center',
  },
  timePickerColumn: {
    flex: 1,
    alignItems: 'center',
  },
  timePickerLabel: {
    color: '#e8f8ff',
    fontSize: 13,
    fontWeight: '700',
    marginBottom: 6,
  },
  timePickerScroll: {
    maxHeight: 200,
    width: '100%',
    borderRadius: 10,
    borderWidth: 1,
    borderColor: 'rgba(136, 214, 255, 0.3)',
    backgroundColor: 'rgba(13, 89, 140, 0.4)',
  },
  timePickerItem: {
    paddingVertical: 10,
    alignItems: 'center',
    borderBottomWidth: 1,
    borderBottomColor: 'rgba(136, 214, 255, 0.15)',
  },
  timePickerItemActive: {
    backgroundColor: '#1b8bce',
  },
  timePickerItemText: {
    color: '#e6f8ff',
    fontSize: 16,
    fontWeight: '600',
  },
  timePickerItemTextActive: {
    color: '#ffffff',
    fontWeight: '800',
  },
  timePickerItemDisabled: {
    backgroundColor: 'rgba(10, 30, 50, 0.3)',
  },
  timePickerItemTextDisabled: {
    color: 'rgba(230, 248, 255, 0.25)',
  },
});
