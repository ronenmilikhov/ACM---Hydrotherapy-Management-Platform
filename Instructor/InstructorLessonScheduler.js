import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
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
};

const toIsoDateString = (dateValue) => {
  if (!(dateValue instanceof Date) || Number.isNaN(dateValue.getTime())) {
    return '';
  }

  const year = dateValue.getFullYear();
  const month = String(dateValue.getMonth() + 1).padStart(2, '0');
  const day = String(dateValue.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const parseIsoDate = (rawValue) => {
  const normalized = String(rawValue || '').trim();
  if (!/^\d{4}-\d{2}-\d{2}$/.test(normalized)) {
    return null;
  }

  const parsedDate = new Date(`${normalized}T00:00:00`);
  if (Number.isNaN(parsedDate.getTime())) {
    return null;
  }

  return parsedDate;
};

const formatDisplayDate = (rawDate) => {
  const parsedDate = parseIsoDate(rawDate);
  if (!parsedDate) {
    return '';
  }

  return parsedDate.toLocaleDateString('he-IL', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
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

  return Math.floor(Date.UTC(year, month - 1, day, 0, 0, 0, 0) / 60000) + minutesFromMidnight;
};

const getIsraelNowMinuteStamp = () => {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Jerusalem',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(new Date());

  const getPartValue = (type) => parts.find((part) => part.type === type)?.value || '';
  const year = Number(getPartValue('year'));
  const month = Number(getPartValue('month'));
  const day = Number(getPartValue('day'));
  const hour = Number(getPartValue('hour'));
  const minute = Number(getPartValue('minute'));

  if (![year, month, day, hour, minute].every(Number.isFinite)) {
    return null;
  }

  return Math.floor(Date.UTC(year, month - 1, day, 0, 0, 0, 0) / 60000) + (hour * 60) + minute;
};

const getIsraelTodayIsoDate = () => {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Jerusalem',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date());

  const getPartValue = (type) => parts.find((part) => part.type === type)?.value || '';
  const year = getPartValue('year');
  const month = getPartValue('month');
  const day = getPartValue('day');

  if (!year || !month || !day) {
    return '';
  }

  return `${year}-${month}-${day}`;
};

const recipientKey = (parentId, childId) => `${parentId}-${childId}`;

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
  const [showDateModal, setShowDateModal] = useState(false);
  const [draftMeetingDate, setDraftMeetingDate] = useState(new Date());

  const [isLoadingPool, setIsLoadingPool] = useState(false);
  const [isLoadingGroups, setIsLoadingGroups] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorText, setErrorText] = useState('');
  const [successText, setSuccessText] = useState('');

  const selectedRecipientItems = useMemo(() => {
    const chosenKeys = new Set(Object.keys(selectedRecipients).filter((key) => selectedRecipients[key]));
    return recipientPool.filter((item) => chosenKeys.has(recipientKey(item.parentId, item.childId)));
  }, [recipientPool, selectedRecipients]);

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

  const onModeChange = (mode) => {
    if (mode === form.mode) {
      return;
    }

    setForm((prev) => ({
      ...prev,
      mode,
      capacity: mode === 'private' ? '1' : (Number(prev.capacity) > 1 ? prev.capacity : '20'),
      minRegistrations: mode === 'private' ? '1' : (Number(prev.minRegistrations) > 1 ? prev.minRegistrations : '5'),
      groupId: mode === 'private' ? '' : prev.groupId,
    }));
    setShowGroupDropdown(false);

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
    const todayAtMidnight = new Date(today.getFullYear(), today.getMonth(), today.getDate());
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

    setDraftMeetingDate(existingDate);
    setShowDateModal(true);
  };

  const saveMeetingDateFromModal = () => {
    setForm((prev) => ({ ...prev, meetingDate: toIsoDateString(draftMeetingDate) }));
    setShowDateModal(false);
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

    if (form.mode === 'group' && (!Number.isInteger(capacity) || capacity <= 0)) {
      setErrorText('קיבולת חייבת להיות מספר חיובי.');
      return;
    }

    if (form.mode === 'group' && (!Number.isInteger(minRegistrations) || minRegistrations <= 0)) {
      setErrorText('מינימום נרשמים חייב להיות מספר חיובי.');
      return;
    }

    if (form.mode === 'group' && minRegistrations > capacity) {
      setErrorText('מינימום נרשמים לא יכול להיות גדול מהקיבולת.');
      return;
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
        minRegistrations: prev.mode === 'private' ? '1' : '5',
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
        leftIcon="‹"
        rightIcon="↻"
        rightLabel="רענון"
        onRightPress={refreshAll}
      />

      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
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
            <TextInput
              value={form.startTime}
              onChangeText={(text) => setForm((prev) => ({ ...prev, startTime: text }))}
              placeholder="התחלה HH:mm"
              placeholderTextColor="#86A2B7"
              style={[styles.input, styles.halfInput]}
              textAlign="right"
            />
          </View>

          {form.mode === 'group' ? (
            <>
              <View style={styles.row}>
                <TextInput
                  value={form.endTime}
                  onChangeText={(text) => setForm((prev) => ({ ...prev, endTime: text }))}
                  placeholder="סיום HH:mm"
                  placeholderTextColor="#86A2B7"
                  style={[styles.input, styles.halfInput]}
                  textAlign="right"
                />
                <TextInput
                  value={form.capacity}
                  onChangeText={(text) => setForm((prev) => ({ ...prev, capacity: text }))}
                  placeholder="קיבולת"
                  placeholderTextColor="#86A2B7"
                  keyboardType="numeric"
                  style={[styles.input, styles.halfInput]}
                  textAlign="right"
                />
              </View>

              <View style={styles.row}>
                <TextInput
                  value={form.minRegistrations}
                  onChangeText={(text) => setForm((prev) => ({ ...prev, minRegistrations: text }))}
                  placeholder="מינימום נרשמים"
                  placeholderTextColor="#86A2B7"
                  keyboardType="numeric"
                  style={[styles.input, styles.halfInput]}
                  textAlign="right"
                />

                <View style={styles.halfInput}>
                  <TouchableOpacity
                    activeOpacity={0.82}
                    style={[styles.input, styles.groupDropdownTrigger]}
                    onPress={() => setShowGroupDropdown((prev) => !prev)}
                  >
                    <Text style={[styles.groupDropdownValue, !selectedGroup && styles.placeholderText]}>
                      {selectedGroup ? selectedGroup.name : 'בחר קבוצה'}
                    </Text>
                  </TouchableOpacity>

                  {showGroupDropdown ? (
                    <View style={styles.groupDropdownMenu}>
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
                    </View>
                  ) : null}
                </View>
              </View>
            </>
          ) : (
            <>
              <TextInput
                value={form.endTime}
                onChangeText={(text) => setForm((prev) => ({ ...prev, endTime: text }))}
                placeholder="סיום HH:mm"
                placeholderTextColor="#86A2B7"
                style={styles.input}
                textAlign="right"
              />
              <View style={styles.privateFixedInfo}>
                <Text style={styles.privateFixedInfoText}>בשיעור פרטי הקיבולת ומינימום הנרשמים נקבעים אוטומטית ל-1.</Text>
              </View>
            </>
          )}

          <TextInput
            value={form.generalNote}
            onChangeText={(text) => setForm((prev) => ({ ...prev, generalNote: text }))}
            placeholder="הערה להורים"
            placeholderTextColor="#86A2B7"
            style={[styles.input, styles.multilineInput]}
            multiline
            textAlign="right"
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
                  setShowGroupDropdown(false);
                  setRecipientDropdownOpen((prev) => !prev);
                }}
              >
                <Text style={[styles.groupDropdownValue, !draftRecipientOption && styles.placeholderText]}>
                  {draftRecipientOption
                    ? `${draftRecipientOption.parentFullName} (${draftRecipientOption.parentEmail || 'ללא מייל'}) • ${draftRecipientOption.childFullName}`
                    : 'בחר הורה וילד'}
                </Text>
              </TouchableOpacity>

              {recipientDropdownOpen ? (
                <ScrollView style={styles.recipientDropdownMenu} nestedScrollEnabled>
                  {recipientPool.map((item) => {
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

        {Platform.OS !== 'android' ? (
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
                  textColor="#e7f7ff"
                  accentColor="#57d1ff"
                  locale="he-IL"
                  minimumDate={parseIsoDate(getIsraelTodayIsoDate()) || new Date(new Date().getFullYear(), new Date().getMonth(), new Date().getDate())}
                  maximumDate={new Date(2100, 11, 31)}
                  onChange={(_event, selectedDate) => {
                    if (selectedDate) {
                      setDraftMeetingDate(selectedDate);
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

      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: '#001529' },
  content: { paddingHorizontal: 16, paddingTop: 16, paddingBottom: 24, gap: 12 },
  panel: {
    backgroundColor: 'rgba(7, 60, 98, 0.68)',
    borderRadius: 16,
    padding: 16,
    borderWidth: 1,
    borderColor: 'rgba(130, 219, 255, 0.3)',
    gap: 8,
  },
  panelTitle: {
    color: '#e7f8ff',
    fontSize: 16,
    fontWeight: '800',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  helperText: {
    color: 'rgba(223, 242, 255, 0.86)',
    fontSize: 12,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  modeRow: {
    flexDirection: 'row-reverse',
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
    flexDirection: 'row-reverse',
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
  },
  dateField: {
    justifyContent: 'center',
  },
  dateFieldText: {
    fontSize: 14,
    color: '#e6f8ff',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  placeholderText: {
    color: 'rgba(193, 229, 250, 0.62)',
  },
  groupDropdownTrigger: {
    justifyContent: 'center',
  },
  groupDropdownValue: {
    fontSize: 14,
    color: '#e6f8ff',
    textAlign: 'right',
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
    textAlign: 'right',
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
    textAlign: 'right',
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
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  halfInput: {
    flex: 1,
  },
  multilineInput: {
    minHeight: 74,
  },
  recipientPickerRow: {
    flexDirection: 'row-reverse',
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
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  recipientOptionSubtitle: {
    marginTop: 3,
    color: 'rgba(205, 235, 255, 0.84)',
    fontSize: 12,
    textAlign: 'right',
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
    color: '#e9f9ff',
    fontSize: 13,
    fontWeight: '800',
    textAlign: 'right',
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
    color: '#e6f8ff',
    fontSize: 13,
    fontWeight: '700',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  selectedRecipientSubtitle: {
    marginTop: 2,
    color: 'rgba(214, 240, 255, 0.86)',
    fontSize: 12,
    textAlign: 'right',
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
    color: 'rgba(220, 241, 255, 0.86)',
    fontSize: 13,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  errorText: {
    color: '#ffd0d8',
    fontSize: 14,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  successText: {
    color: '#abffe0',
    fontSize: 14,
    textAlign: 'right',
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
    color: '#e8f8ff',
    fontSize: 16,
    fontWeight: '800',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  datePickerControl: {
    backgroundColor: 'rgba(9, 76, 122, 0.88)',
    borderRadius: 12,
  },
  dateModalActions: {
    flexDirection: 'row-reverse',
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
});
