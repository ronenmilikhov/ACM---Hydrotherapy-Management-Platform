import 'react-native-gesture-handler';
import React, { useCallback, useEffect, useRef } from 'react';
import { I18nManager } from 'react-native';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { NavigationContainer } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';

// Force RTL layout for Hebrew.
I18nManager.allowRTL(true);
I18nManager.forceRTL(true);
import InstructorHomepage from './Instructor/InstructorHomepage';
import SelectGroup from './Instructor/SelectGroup';
import GroupDetails from './Instructor/GroupDetails';
import ChildList from './Instructor/ChildList';
import ChildProfile from './Instructor/ChildProfile';
import ManagerHomepage from './Manager/ManagerHomepage';
import ManagerSystemReports from './Manager/ManagerSystemReports';
import ManagerAttendanceReport from './Manager/ManagerAttendanceReport';
import ParentHomepage from './Parent/ParentHomepage';
import ChatPage from './ChatPage';
import ParentProgressReport from './Parent/ParentProgressReport';
import ParentLessonHistory from './Parent/ParentLessonHistory';
import EditAchievement from './Instructor/EditAchievement';
import InstructorProgressReport from './Instructor/InstructorProgressReport';
import InstructorConversationInbox from './Instructor/InstructorConversationInbox';
import InstructorLessonScheduler from './Instructor/InstructorLessonScheduler';
import InstructorLessonBoard from './Instructor/InstructorLessonBoard';
import InstructorNotificationsInbox from './Instructor/InstructorNotificationsInbox';
import ParentNotificationsInbox from './Parent/ParentNotificationsInbox';
import InstructorAIGroupReports from './Instructor/InstructorAIGroupReports';
import {
  initializeNotifications,
  addNotificationReceivedListener,
  addNotificationResponseReceivedListener,
  getLastNotificationResponseAsync,
} from './notifications/pushNotifications';
import { API_BASE_URL } from './apiConfig';
//import CreateGroup from './Instructor/CreateGroup';
import LoginPage from './LoginPage';

const Stack = createNativeStackNavigator();

// Initialize notification handler immediately at module load
// so that any push notification received (even before navigation
// is ready) will be shown in the system notification center.
initializeNotifications().catch(() => {});

export default function App() {
  const navigationRef = useRef(null);
  const isNavigationReadyRef = useRef(false);
  const pendingNotificationResponseRef = useRef(null);
  const handledNotificationResponseIdsRef = useRef(new Set());

  const inferRecipientUserType = useCallback((notificationType, data) => {
    const explicitUserType = String(data?.userType || '').trim().toLowerCase();
    if (explicitUserType === 'instructor' || explicitUserType === 'parent') {
      return explicitUserType;
    }

    if (notificationType === 'chat_message') {
      const senderType = String(data?.senderType || '').trim().toLowerCase();
      if (senderType === 'parent') {
        return 'instructor';
      }

      if (senderType === 'instructor') {
        return 'parent';
      }
    }

    if (notificationType === 'instructor_broadcast') {
      return 'parent';
    }

    const hasParentId = Number(data?.parentId || 0) > 0;
    const hasInstructorId = Number(data?.instructorId || 0) > 0;

    if (hasParentId && !hasInstructorId) {
      return 'instructor';
    }

    if (hasInstructorId && !hasParentId) {
      return 'parent';
    }

    return '';
  }, []);

  const navigateToRoleHomepage = useCallback((recipientUserType) => {
    const nav = navigationRef.current;
    if (!nav) {
      return false;
    }

    if (recipientUserType === 'instructor') {
      nav.navigate('InstructorHomepage', {});
      return true;
    }

    if (recipientUserType === 'parent') {
      nav.navigate('ParentHomepage', {});
      return true;
    }

    return false;
  }, []);

  const openChatFromPushNotification = useCallback(async (data, recipientUserType) => {
    const nav = navigationRef.current;
    if (!nav) {
      return false;
    }

    const conversationId = Number(data?.conversationId || 0);
    if (conversationId <= 0) {
      return false;
    }

    let conversationPayload = null;

    try {
      const response = await fetch(`${API_BASE_URL}/chat/conversations/${conversationId}`);
      conversationPayload = await response.json().catch(() => null);
      if (!response.ok) {
        return false;
      }
    } catch (_error) {
      return false;
    }

    const parentId = Number(conversationPayload?.parentId || 0);
    const childId = Number(conversationPayload?.childId || 0);
    const instructorId = Number(conversationPayload?.instructorId || 0);

    if (parentId <= 0 || childId <= 0 || instructorId <= 0) {
      return false;
    }

    const fallbackSenderType = String(data?.senderType || '').trim().toLowerCase();
    const fromInstructor = recipientUserType
      ? recipientUserType === 'instructor'
      : fallbackSenderType === 'parent';

    nav.navigate('ChatPage', {
      fromInstructor,
      conversationId,
      parentId,
      childId,
      instructorId,
      parentName: String(conversationPayload?.parentFullName || '').trim() || 'הורה',
      childName: String(conversationPayload?.childFullName || '').trim() || 'ילד',
      instructorName: String(conversationPayload?.instructorFullName || '').trim() || 'מדריך',
    });

    return true;
  }, []);

  const processNotificationResponse = useCallback(async (response) => {
    const responseId = String(response?.notification?.request?.identifier || '').trim();
    if (responseId && handledNotificationResponseIdsRef.current.has(responseId)) {
      return;
    }

    if (!isNavigationReadyRef.current || !navigationRef.current) {
      pendingNotificationResponseRef.current = response;
      return;
    }

    if (responseId) {
      handledNotificationResponseIdsRef.current.add(responseId);
    }

    const data = response?.notification?.request?.content?.data;
    if (!data) {
      return;
    }

    const notificationType = String(data?.type || '').trim().toLowerCase();
    const recipientUserType = inferRecipientUserType(notificationType, data);

    if (notificationType === 'chat_message') {
      const didOpenChat = await openChatFromPushNotification(data, recipientUserType);
      if (didOpenChat) {
        return;
      }
    }

    navigateToRoleHomepage(recipientUserType);
  }, [inferRecipientUserType, navigateToRoleHomepage, openChatFromPushNotification]);

  const handleNavigationReady = useCallback(() => {
    isNavigationReadyRef.current = true;

    const pendingResponse = pendingNotificationResponseRef.current;
    if (pendingResponse) {
      pendingNotificationResponseRef.current = null;
      void processNotificationResponse(pendingResponse);
    }
  }, [processNotificationResponse]);

  useEffect(() => {
    // Listen for notifications received while the app is in the foreground.
    // The setNotificationHandler already ensures they appear in the
    // notification center; this listener can be used for additional
    // in-app behavior (e.g. refreshing badge counts).
    const receivedSubscription = addNotificationReceivedListener((notification) => {
      const data = notification?.request?.content?.data;
      console.log('[push] notification received in foreground', {
        type: data?.type || 'unknown',
        title: notification?.request?.content?.title || '',
      });
    });

    // Listen for user taps on notifications from the notification center.
    // Navigate to chat for chat notifications; fallback to role homepage otherwise.
    const responseSubscription = addNotificationResponseReceivedListener((response) => {
      void processNotificationResponse(response);
    });

    // Handle cold-start launch from a tapped notification.
    getLastNotificationResponseAsync()
      .then((lastResponse) => {
        if (lastResponse) {
          void processNotificationResponse(lastResponse);
        }
      })
      .catch(() => {});

    return () => {
      receivedSubscription.remove();
      responseSubscription.remove();
    };
  }, [processNotificationResponse]);

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <NavigationContainer ref={navigationRef} onReady={handleNavigationReady}>
        <Stack.Navigator initialRouteName="Login" screenOptions={{ headerShown: false }}>
          <Stack.Screen name="Login" component={LoginPage} />
          <Stack.Screen name="InstructorHomepage" component={InstructorHomepage} />
          <Stack.Screen name="SelectGroup" component={SelectGroup} />
          <Stack.Screen name="GroupDetails" component={GroupDetails} />
          <Stack.Screen name="ChildList" component={ChildList} />
          <Stack.Screen name="ChildProfile" component={ChildProfile} />
          <Stack.Screen name="ManagerHomepage" component={ManagerHomepage} />
          <Stack.Screen name="ManagerSystemReports" component={ManagerSystemReports} />
          <Stack.Screen name="ManagerAttendanceReport" component={ManagerAttendanceReport} />
          <Stack.Screen name="ParentHomepage" component={ParentHomepage} />
          <Stack.Screen name="ChatPage" component={ChatPage} />
          <Stack.Screen name="ParentProgressReport" component={ParentProgressReport} />
          <Stack.Screen name="ParentLessonHistory" component={ParentLessonHistory} />
          <Stack.Screen name="EditAchievement" component={EditAchievement} />
          <Stack.Screen name="InstructorProgressReport" component={InstructorProgressReport} />
          <Stack.Screen name="InstructorConversationInbox" component={InstructorConversationInbox} />
          <Stack.Screen name="InstructorLessonScheduler" component={InstructorLessonScheduler} />
          <Stack.Screen name="InstructorLessonBoard" component={InstructorLessonBoard} />
          <Stack.Screen name="InstructorNotificationsInbox" component={InstructorNotificationsInbox} />
          <Stack.Screen name="InstructorAIGroupReports" component={InstructorAIGroupReports} />
          <Stack.Screen name="ParentNotificationsInbox" component={ParentNotificationsInbox} />
        </Stack.Navigator>
      </NavigationContainer>
    </GestureHandlerRootView>
  );
}
