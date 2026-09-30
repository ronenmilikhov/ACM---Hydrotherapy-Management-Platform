import React, { useCallback, useEffect, useState } from 'react';
import {
  Platform,
  SafeAreaView,
  ScrollView,
  StatusBar,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
  I18nManager,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { useFocusEffect } from '@react-navigation/native';
import AquaticBackground from '../components/ui/AquaticBackground';
import RoleHeader from '../components/ui/RoleHeader';
import PrimaryButton from '../components/ui/PrimaryButton';
import { API_BASE_URL } from '../apiConfig';

const ISRAEL_TIME_ZONE = 'Asia/Jerusalem';

const parseUtcLikeDate = (rawValue) => {
  const normalized = String(rawValue ?? '').trim();
  if (!normalized) {
    return new Date('');
  }

  const hasExplicitTimezone = /(?:[zZ]|[+\-]\d{2}:\d{2})$/.test(normalized);
  const isIsoDateTimeWithoutTimezone = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?$/.test(normalized);
  const normalizedForParsing = (!hasExplicitTimezone && isIsoDateTimeWithoutTimezone)
    ? `${normalized}Z`
    : normalized;

  return new Date(normalizedForParsing);
};

const formatDateTime = (rawValue) => {
  const parsed = parseUtcLikeDate(rawValue);
  if (Number.isNaN(parsed.getTime())) {
    return 'לא זמין';
  }

  const formatOptions = {
    day: '2-digit',
    month: '2-digit',
    year: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: ISRAEL_TIME_ZONE,
  };

  try {
    return parsed.toLocaleString('he-IL', formatOptions);
  } catch (_error) {
    return parsed.toLocaleString('he-IL', {
      day: '2-digit',
      month: '2-digit',
      year: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
  }
};

const formatThreadTitle = (thread) => {
  const parentName = String(thread?.parentFullName || '').trim() || 'הורה';
  const childName = String(thread?.childFullName || '').trim();
  if (!childName) {
    return parentName;
  }

  return `${parentName} (ההורה של ${childName})`;
};

export default function InstructorConversationInbox({ navigation, route }) {
  const authUser = route?.params?.authUser;
  const instructorId = Number(authUser?.id || 0);

  const [threads, setThreads] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [errorText, setErrorText] = useState('');
  const [searchText, setSearchText] = useState('');

  const filteredThreads = threads.filter((thread) => {
    const parentName = String(thread?.parentFullName || '').trim().toLowerCase();
    const query = searchText.trim().toLowerCase();
    return parentName.includes(query);
  });

  const loadInbox = useCallback(async () => {
    if (!instructorId) {
      setThreads([]);
      setErrorText('לא זוהה מדריך מחובר. התחברו מחדש.');
      return;
    }

    try {
      setIsLoading(true);
      setErrorText('');

      const response = await fetch(`${API_BASE_URL}/chat/instructor/${instructorId}/inbox`);
      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'לא ניתן לטעון תיבת צ׳אטים כרגע.');
      }

      setThreads(Array.isArray(payload) ? payload : []);
    } catch (error) {
      setThreads([]);
      setErrorText(error?.message || 'לא ניתן לטעון תיבת צ׳אטים כרגע.');
    } finally {
      setIsLoading(false);
    }
  }, [instructorId]);

  useEffect(() => {
    loadInbox();
  }, [loadInbox]);

  useFocusEffect(
    useCallback(() => {
      loadInbox();
    }, [loadInbox]),
  );

  return (
    <SafeAreaView style={styles.safeArea}>
      <StatusBar translucent backgroundColor="transparent" barStyle="light-content" />
      <AquaticBackground variant="instructor" showWaves={false} />

      <RoleHeader
        title="תיבת צ׳אטים"
        onMenuPress={() => navigation.goBack()}
        leftIcon="›"
        rightIcon="↻"
        rightLabel="רענון"
        onRightPress={loadInbox}
        theme="dark"
      />

      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        <View style={styles.summaryPanel}>
          <Text style={styles.summaryTitle}>שיחות פעילות עם הורים</Text>
          <Text style={styles.summaryText}>כל שיחה נפתחת ישירות לצ׳אט המלא עם ההורה עבור הילד המתאים.</Text>
          <Text style={styles.summaryCount}>סה״כ שיחות: {threads.length}</Text>
        </View>

        <TextInput
          value={searchText}
          onChangeText={setSearchText}
          style={styles.searchInput}
          placeholder="חיפוש שיחה לפי שם ההורה..."
          placeholderTextColor="rgba(220, 242, 255, 0.5)"
          textAlign="right"
        />

        {isLoading ? (
          <View style={styles.stateCard}>
            <Text style={styles.stateText}>טוען שיחות...</Text>
          </View>
        ) : null}

        {!isLoading && errorText ? (
          <View style={styles.stateCardError}>
            <Text style={styles.errorText}>{errorText}</Text>
          </View>
        ) : null}

        {!isLoading && !errorText && threads.length === 0 ? (
          <View style={styles.stateCard}>
            <Text style={styles.stateText}>עדיין אין שיחות עם הודעה ראשונה מהורים.</Text>
          </View>
        ) : null}

        {!isLoading && !errorText && threads.length > 0 && filteredThreads.length === 0 ? (
          <View style={styles.stateCard}>
            <Text style={styles.stateText}>לא נמצאו שיחות התואמות לחיפוש.</Text>
          </View>
        ) : null}

        {filteredThreads.map((thread) => (
          <TouchableOpacity
            key={String(thread.conversationId)}
            activeOpacity={0.82}
            style={styles.threadCardShell}
            onPress={() => navigation.navigate('ChatPage', {
              fromInstructor: true,
              conversationId: thread.conversationId,
              parentId: thread.parentId,
              childId: thread.childId,
              instructorId: thread.instructorId,
              parentName: thread.parentFullName,
              childName: thread.childFullName,
              instructorName: authUser?.fullName || 'מדריך',
            })}
          >
            <LinearGradient
              colors={['rgba(56, 164, 230, 0.96)', 'rgba(23, 104, 170, 0.96)']}
              start={{ x: 0, y: 0 }}
              end={{ x: 1, y: 1 }}
              style={styles.threadCard}
            >
              <View style={styles.threadHeaderRow}>
                <Text style={styles.threadTitle}>{formatThreadTitle(thread)}</Text>
                <View style={styles.threadBadge}>
                  <Text style={styles.threadBadgeText}>צ׳אט</Text>
                </View>
              </View>

              <Text style={styles.threadBody} numberOfLines={2}>{thread.lastMessageText || 'ללא הודעה'}</Text>
              <Text style={styles.threadMeta}>הודעה אחרונה: {formatDateTime(thread.lastMessageAt)}</Text>
            </LinearGradient>
          </TouchableOpacity>
        ))}

        <PrimaryButton
          label="חזרה לדף המדריך"
          onPress={() => navigation.navigate('InstructorHomepage', { authUser })}
          style={styles.backButtonShell}
          gradientStyle={styles.backButton}
          textStyle={styles.backButtonText}
          colorsOverride={['#2E77BC', '#255E97']}
        />
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: '#001529', paddingTop: Platform.OS === 'ios' ? 44 : (Platform.OS === 'android' ? StatusBar.currentHeight : 0) },
  content: { paddingHorizontal: 18, paddingTop: 8, paddingBottom: 48, gap: 12 },
  summaryPanel: {
    backgroundColor: 'rgba(8, 60, 97, 0.66)',
    borderRadius: 16,
    padding: 14,
    borderWidth: 1,
    borderColor: 'rgba(130, 219, 255, 0.34)',
    gap: 6,
  },
  summaryTitle: {
    color: '#eaf9ff',
    fontSize: 17,
    fontWeight: '800',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  summaryText: {
    color: 'rgba(220, 242, 255, 0.85)',
    fontSize: 13,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  summaryCount: {
    color: '#93e1ff',
    fontSize: 13,
    fontWeight: '700',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  searchInput: {
    backgroundColor: 'rgba(8, 60, 97, 0.45)',
    borderRadius: 12,
    borderWidth: 1,
    borderColor: 'rgba(130, 219, 255, 0.28)',
    paddingHorizontal: 16,
    paddingVertical: 10,
    color: '#eaf9ff',
    fontSize: 14,
    textAlign: 'right',
    marginTop: 4,
    marginBottom: 4,
  },
  stateCard: {
    backgroundColor: 'rgba(6, 58, 95, 0.62)',
    borderWidth: 1,
    borderColor: 'rgba(130, 219, 255, 0.28)',
    borderRadius: 14,
    paddingVertical: 12,
    paddingHorizontal: 12,
  },
  stateCardError: {
    backgroundColor: 'rgba(116, 33, 52, 0.42)',
    borderWidth: 1,
    borderColor: 'rgba(255, 166, 181, 0.34)',
    borderRadius: 14,
    paddingVertical: 12,
    paddingHorizontal: 12,
  },
  stateText: {
    color: 'rgba(235,248,255,0.9)',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
    fontSize: 14,
  },
  errorText: {
    color: '#ffd5de',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
    fontSize: 14,
  },
  threadCardShell: {
    borderRadius: 16,
    overflow: 'hidden',
    borderWidth: 1,
    borderColor: 'rgba(143, 223, 255, 0.35)',
    shadowColor: '#0b6db2',
    shadowOffset: { width: 0, height: 8 },
    shadowOpacity: 0.25,
    shadowRadius: 14,
    elevation: 4,
  },
  threadCard: {
    padding: 14,
  },
  threadHeaderRow: {
    flexDirection: I18nManager.isRTL ? 'row' : 'row-reverse',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 10,
  },
  threadBadge: {
    borderRadius: 999,
    paddingHorizontal: 10,
    paddingVertical: 4,
    backgroundColor: 'rgba(255,255,255,0.2)',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.28)',
  },
  threadBadgeText: {
    color: '#e8f8ff',
    fontSize: 11,
    fontWeight: '800',
    writingDirection: 'rtl',
  },
  threadTitle: {
    color: '#f3fbff',
    fontSize: 16,
    fontWeight: '800',
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
    flex: 1,
  },
  threadBody: {
    marginTop: 8,
    color: 'rgba(233,247,255,0.94)',
    fontSize: 13,
    lineHeight: 20,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  threadMeta: {
    marginTop: 8,
    color: 'rgba(207,237,255,0.9)',
    fontSize: 12,
    textAlign: Platform.OS === 'ios' ? 'left' : 'right',
    writingDirection: 'rtl',
  },
  backButtonShell: { marginTop: 8 },
  backButton: { paddingVertical: 14 },
  backButtonText: { color: '#FFFFFF', fontSize: 16, fontWeight: '700' },
});
