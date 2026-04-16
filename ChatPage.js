import React, { useState, useEffect, useRef, useMemo, useCallback } from 'react';
import {
  StyleSheet, Text, View, Animated, Dimensions, TouchableOpacity,
  TextInput, StatusBar, Easing, KeyboardAvoidingView, Platform, FlatList, Alert, Image, Modal,
} from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { BlurView } from 'expo-blur';
import Svg, { Path } from 'react-native-svg';
import { PanGestureHandler, PinchGestureHandler, State as GestureState } from 'react-native-gesture-handler';
import * as ImagePicker from 'expo-image-picker';
import { colors, shadows } from './theme/tokens';
import { API_BASE_URL } from './apiConfig';
import { markParentChatNotificationsAsRead } from './notifications/parentNotifications';
import { registerPushNotificationsForUser } from './notifications/pushNotifications';

const { width, height } = Dimensions.get('window');
const AnimatedBlurView = Animated.createAnimatedComponent(BlurView);
const MESSAGE_POLL_INTERVAL_MS = 2500;
const CHAT_ATTACHMENT_PREFIX = '[chat-attachment]';
const ISRAEL_TIME_ZONE = 'Asia/Jerusalem';
const LOCAL_ECHO_GRACE_MS = 20000;

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

const formatChatMessageTime = (rawValue) => {
  const parsed = parseUtcLikeDate(rawValue);
  if (Number.isNaN(parsed.getTime())) {
    return '--:--';
  }

  try {
    return parsed.toLocaleTimeString('he-IL', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
      timeZone: ISRAEL_TIME_ZONE,
    });
  } catch (_error) {
    return parsed.toLocaleTimeString('he-IL', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
  }
};

// Derive the server origin from API_BASE_URL by stripping the trailing "/api" path.
// e.g. "http://192.168.1.13:5202/api" → "http://192.168.1.13:5202"
const SERVER_ORIGIN = (() => {
  const base = String(API_BASE_URL || '').trim().replace(/\/+$/, '');
  if (base.endsWith('/api')) {
    return base.slice(0, -4);
  }
  // Fallback: strip last path segment
  const lastSlash = base.lastIndexOf('/');
  const schemeSep = base.indexOf('://');
  if (lastSlash > schemeSep + 2) {
    return base.slice(0, lastSlash);
  }
  return base;
})();

/**
 * Resolve an attachment URL that may be either:
 *  - A relative path like `/chat-attachments/file.jpg`
 *  - An absolute URL like `http://host:port/chat-attachments/file.jpg`
 *
 * In both cases we normalise it so the current client can reach the file
 * via its own configured server origin.
 */
const resolveAttachmentUrl = (rawUrl) => {
  const url = String(rawUrl || '').trim();
  if (!url) return '';

  // Base64 data URLs are self-contained – pass through unchanged.
  if (url.startsWith('data:')) {
    return url;
  }

  let resolved = '';

  // Already a full URL with a scheme – rewrite its origin to match the client's config
  if (/^https?:\/\//i.test(url)) {
    try {
      const parsed = new URL(url);
      // Keep only the pathname (+ search/hash if any)
      resolved = `${SERVER_ORIGIN}${parsed.pathname}${parsed.search}${parsed.hash}`;
    } catch (_e) {
      resolved = url;
    }
  } else if (url.startsWith('/')) {
    // Relative path
    resolved = `${SERVER_ORIGIN}${url}`;
  } else {
    // Something unexpected – return as-is
    resolved = url;
  }

  // Append a cache-busting parameter so React Native never serves a stale
  // or previously-failed cached response for this image.
  if (resolved) {
    const separator = resolved.includes('?') ? '&' : '?';
    resolved = `${resolved}${separator}_cb=${Date.now()}`;
  }

  return resolved;
};

const parseChatMessagePayload = (rawMessageText) => {
  const normalizedText = String(rawMessageText || '').trim();
  if (!normalizedText.startsWith(CHAT_ATTACHMENT_PREFIX)) {
    return {
      text: normalizedText,
      attachmentType: '',
      attachmentUrl: '',
    };
  }

  const payloadJson = normalizedText.slice(CHAT_ATTACHMENT_PREFIX.length).trim();
  if (!payloadJson) {
    return {
      text: '',
      attachmentType: '',
      attachmentUrl: '',
    };
  }

  try {
    const payload = JSON.parse(payloadJson);
    const attachmentType = String(payload?.type ?? payload?.Type ?? '').trim().toLowerCase();
    const attachmentUrl = String(payload?.url ?? payload?.Url ?? '').trim();
    const captionText = String(payload?.caption ?? payload?.Caption ?? '').trim();

    if (attachmentType === 'image' && attachmentUrl) {
      return {
        text: captionText,
        attachmentType,
        attachmentUrl: resolveAttachmentUrl(attachmentUrl),
      };
    }
  } catch (_error) {
    return {
      text: normalizedText,
      attachmentType: '',
      attachmentUrl: '',
    };
  }

  return {
    text: normalizedText,
    attachmentType: '',
    attachmentUrl: '',
  };
};

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
// 2. INFINITE REINCARNATING BUBBLES
// ========================================
const Bubble = () => {
  const progress = useRef(new Animated.Value(0)).current;
  const sway = useRef(new Animated.Value(0)).current;

  const [config, setConfig] = useState({
    size: Math.random() * 30 + 10,
    left: Math.random() * width,
  });

  const durationRef = useRef(Math.random() * 16000 + 14000);
  const isFirstRun = useRef(true);

  const startFloat = () => {
    progress.setValue(0);
    const delay = isFirstRun.current ? Math.random() * 30000 : 0;
    isFirstRun.current = false;

    Animated.sequence([
      Animated.delay(delay),
      Animated.timing(progress, {
        toValue: 1,
        duration: durationRef.current,
        easing: Easing.linear,
        useNativeDriver: true,
      }),
    ]).start(({ finished }) => {
      if (finished) {
        durationRef.current = Math.random() * 16000 + 14000;
        setConfig({
          size: Math.random() * 30 + 10,
          left: Math.random() * width,
        });
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
  const translateXVal = sway.interpolate({ inputRange: [-1, 1], outputRange: [-20, 20] });

  return (
    <Animated.View
      style={[
        styles.bubbleWrapper,
        {
          width: config.size,
          height: config.size,
          borderRadius: config.size / 2,
          left: config.left,
          transform: [{ translateY }, { translateX: translateXVal }],
          opacity,
        },
      ]}
    >
      <LinearGradient
        colors={["rgba(255,255,255,0.95)", "rgba(255,255,255,0.1)"]}
        style={{ flex: 1, borderRadius: config.size / 2, borderWidth: 1, borderColor: "rgba(255,255,255,0.4)" }}
      />
    </Animated.View>
  );
};

// ========================================
// 3. CHAT ATTACHMENT IMAGE (with auto-retry)
// ========================================
const ChatAttachmentImage = ({ uri, style, resizeMode, onPress }) => {
  const MAX_RETRIES = 3;
  const RETRY_DELAY_MS = 1500;

  const [retryCount, setRetryCount] = useState(0);
  const [imageUri, setImageUri] = useState(uri);
  const [isLoading, setIsLoading] = useState(true);
  const [hasFailed, setHasFailed] = useState(false);

  useEffect(() => {
    // When the source uri changes externally, reset state.
    setImageUri(uri);
    setRetryCount(0);
    setIsLoading(true);
    setHasFailed(false);
  }, [uri]);

  const isDataUri = String(uri || '').startsWith('data:');

  const handleLoadError = useCallback(() => {
    // Data URIs are self-contained; retrying won't help if they fail to parse.
    if (isDataUri || retryCount >= MAX_RETRIES) {
      setIsLoading(false);
      setHasFailed(true);
      return;
    }

    const nextRetry = retryCount + 1;
    setTimeout(() => {
      // Build a brand-new URL with a fresh cache-busting parameter so RN
      // doesn't serve the stale/failed cached response.
      const baseUri = String(uri || '').replace(/[?&]_cb=\d+/, '');
      const separator = baseUri.includes('?') ? '&' : '?';
      setImageUri(`${baseUri}${separator}_cb=${Date.now()}`);
      setRetryCount(nextRetry);
      setIsLoading(true);
      setHasFailed(false);
    }, RETRY_DELAY_MS * nextRetry);
  }, [uri, retryCount, isDataUri]);

  const handleManualRetry = useCallback(() => {
    if (isDataUri) {
      // For data URIs, just reset and try the same URI again
      setImageUri(uri);
    } else {
      const baseUri = String(uri || '').replace(/[?&]_cb=\d+/, '');
      const separator = baseUri.includes('?') ? '&' : '?';
      setImageUri(`${baseUri}${separator}_cb=${Date.now()}`);
    }
    setRetryCount(0);
    setIsLoading(true);
    setHasFailed(false);
  }, [uri, isDataUri]);

  return (
    <TouchableOpacity
      activeOpacity={0.9}
      style={styles.msgAttachmentWrap}
      onPress={() => hasFailed ? handleManualRetry() : onPress?.(imageUri)}
    >
      {!hasFailed && (
        <Image
          source={{ uri: imageUri, cache: 'reload' }}
          style={style}
          resizeMode={resizeMode}
          onLoad={() => setIsLoading(false)}
          onError={handleLoadError}
        />
      )}
      {isLoading && !hasFailed && (
        <View style={styles.msgAttachmentLoading}>
          <Text style={styles.msgAttachmentLoadingText}>🔄</Text>
        </View>
      )}
      {hasFailed && (
        <View style={[style, styles.msgAttachmentError]}>
          <Text style={styles.msgAttachmentErrorIcon}>📷</Text>
          <Text style={styles.msgAttachmentErrorText}>לא ניתן לטעון תמונה</Text>
          <Text style={styles.msgAttachmentRetryText}>לחץ לנסות שוב</Text>
        </View>
      )}
    </TouchableOpacity>
  );
};

// ========================================
// 3b. ANIMATED MESSAGE BUBBLE
// ========================================
const MessageBubble = ({ item, isMe, onImagePress }) => {
  const fadeAnim = useRef(new Animated.Value(0)).current;
  const slideAnim = useRef(new Animated.Value(isMe ? 30 : -30)).current;
  const hasImageAttachment = item.attachmentType === 'image' && Boolean(item.attachmentUrl);

  useEffect(() => {
    Animated.parallel([
      Animated.timing(fadeAnim, { toValue: 1, duration: 350, useNativeDriver: true }),
      Animated.timing(slideAnim, {
        toValue: 0, duration: 350,
        easing: Easing.out(Easing.quad), useNativeDriver: true,
      }),
    ]).start();
  }, []);

  const timeStr = formatChatMessageTime(item.timestamp);

  return (
    <Animated.View
      style={[
        styles.msgRow,
        isMe ? styles.msgRowRight : styles.msgRowLeft,
        { opacity: fadeAnim, transform: [{ translateX: slideAnim }] },
      ]}
    >
      {/* Sender avatar for received messages */}
      {!isMe && (
        <LinearGradient colors={['#00d4ff', '#0077aa']} style={styles.msgAvatar}>
          <Text style={styles.msgAvatarText}>{item.senderName?.[0] || '?'}</Text>
        </LinearGradient>
      )}

      <BlurView intensity={20} tint="dark" style={[
        styles.msgBubble,
        isMe ? styles.msgBubbleMe : styles.msgBubbleOther,
      ]}>
        {!isMe && (
          <Text style={styles.msgSender}>{item.senderName}</Text>
        )}

        {hasImageAttachment ? (
          <ChatAttachmentImage
            uri={item.attachmentUrl}
            style={styles.msgAttachmentImage}
            resizeMode="cover"
            onPress={onImagePress}
          />
        ) : null}

        {item.text ? (
          <Text style={styles.msgText}>{item.text}</Text>
        ) : null}

        <Text style={[styles.msgTime, isMe && styles.msgTimeMe]}>{timeStr}</Text>

        {item.isPending ? (
          <Text style={[styles.msgDeliveryState, styles.msgDeliveryPending]}>שולח...</Text>
        ) : null}

        {item.isFailed ? (
          <Text style={[styles.msgDeliveryState, styles.msgDeliveryFailed]}>לא נשלח</Text>
        ) : null}
      </BlurView>

      {/* Sender avatar for sent messages */}
      {isMe && (
        <LinearGradient colors={['#00b8d4', '#0099cc']} style={styles.msgAvatar}>
          <Text style={styles.msgAvatarText}>{item.senderName?.[0] || '?'}</Text>
        </LinearGradient>
      )}
    </Animated.View>
  );
};

// ========================================
// 4. MAIN CHAT PAGE
// ========================================
export default function ChatPage({ navigation, route }) {
  const fromInstructor = route?.params?.fromInstructor ?? false;
  const initialConversationId = Number(route?.params?.conversationId ?? 0);
  const parentId = Number(route?.params?.parentId ?? 0);
  const childId = Number(route?.params?.childId ?? 0);
  const instructorId = Number(route?.params?.instructorId ?? 0);
  const parentName = route?.params?.parentName || 'הורה';
  const instructorName = route?.params?.instructorName || 'מדריך';
  const childName = route?.params?.childName || '';

  const hasLiveConversation = parentId > 0 && childId > 0 && instructorId > 0;

  const currentUserId = hasLiveConversation
    ? (fromInstructor ? `instructor-${instructorId}` : `parent-${parentId}`)
    : (fromInstructor ? 'instructor1' : 'parent1');

  const headerCounterpartLine = hasLiveConversation
    ? (fromInstructor
      ? `הורה: ${(parentName || '').trim() || 'לא זמין'}`
      : `מדריך: ${(instructorName || '').trim() || 'לא זמין'}`)
    : (fromInstructor
      ? `הורה: ${(parentName || '').trim() || 'לא זמין'}`
      : 'מדריך: שי שוורצנגר');

  const headerChildLine = hasLiveConversation
    ? `ילד: ${(childName || '').trim() || 'לא זמין'}`
    : 'ילד: ללא בחירה';

  const [conversationId, setConversationId] = useState(null);
  const [isBootstrapping, setIsBootstrapping] = useState(false);
  const [isSending, setIsSending] = useState(false);
  const [messages, setMessages] = useState([]);
  const [input, setInput] = useState('');
  const [pendingAttachment, setPendingAttachment] = useState(null);
  const [zoomImageUri, setZoomImageUri] = useState('');
  const [isImagePannable, setIsImagePannable] = useState(false);
  const zoomBaseScale = useRef(new Animated.Value(1)).current;
  const zoomPinchScale = useRef(new Animated.Value(1)).current;
  const zoomBaseTranslateX = useRef(new Animated.Value(0)).current;
  const zoomBaseTranslateY = useRef(new Animated.Value(0)).current;
  const zoomPanTranslateX = useRef(new Animated.Value(0)).current;
  const zoomPanTranslateY = useRef(new Animated.Value(0)).current;
  const zoomLastScaleRef = useRef(1);
  const zoomLastPanRef = useRef({ x: 0, y: 0 });
  const pinchGestureRef = useRef(null);
  const panGestureRef = useRef(null);
  const zoomScale = useMemo(
    () => Animated.multiply(zoomBaseScale, zoomPinchScale),
    [zoomBaseScale, zoomPinchScale]
  );
  const zoomTranslateX = useMemo(
    () => Animated.add(zoomBaseTranslateX, zoomPanTranslateX),
    [zoomBaseTranslateX, zoomPanTranslateX]
  );
  const zoomTranslateY = useMemo(
    () => Animated.add(zoomBaseTranslateY, zoomPanTranslateY),
    [zoomBaseTranslateY, zoomPanTranslateY]
  );
  const onZoomGestureEvent = useMemo(
    () => Animated.event([{ nativeEvent: { scale: zoomPinchScale } }], { useNativeDriver: true }),
    [zoomPinchScale]
  );
  const onPanGestureEvent = useMemo(
    () => Animated.event(
      [{ nativeEvent: { translationX: zoomPanTranslateX, translationY: zoomPanTranslateY } }],
      { useNativeDriver: true }
    ),
    [zoomPanTranslateX, zoomPanTranslateY]
  );
  const flatListRef = useRef(null);
  const didInitialAutoScrollRef = useRef(false);
  const latestMessagesRequestIdRef = useRef(0);
  const scrollY = useRef(new Animated.Value(0)).current;
  const bubblePool = useMemo(() => Array.from({ length: 5 }).map((_, i) => i), []);

  const scrollToLatestMessage = useCallback((animated = false) => {
    requestAnimationFrame(() => {
      flatListRef.current?.scrollToEnd({ animated });
    });
  }, []);

  // Typing indicator pulse
  const typingPulse = useRef(new Animated.Value(0.4)).current;
  const [isTyping, setIsTyping] = useState(false);

  const mapApiMessageToUi = useCallback((message) => {
    const senderType = String(message?.senderType || message?.SenderType || '').toLowerCase();
    const isParentSender = senderType === 'parent';
    const parsedPayload = parseChatMessagePayload(message?.messageText || message?.MessageText || '');

    return {
      id: String(message?.id ?? message?.Id ?? Date.now()),
      senderId: isParentSender ? `parent-${parentId}` : `instructor-${instructorId}`,
      senderName: message?.senderName || message?.SenderName || (isParentSender ? parentName : instructorName),
      text: parsedPayload.text,
      attachmentType: parsedPayload.attachmentType,
      attachmentUrl: parsedPayload.attachmentUrl,
      timestamp: message?.sentAt || message?.SentAt || new Date().toISOString(),
    };
  }, [parentId, instructorId, parentName, instructorName]);

  const clearPendingAttachment = useCallback(() => {
    setPendingAttachment(null);
  }, []);

  const clampZoomOffset = useCallback((x, y, scale) => {
    const safeScale = Math.max(1, Number(scale || 1));
    if (safeScale <= 1.01) {
      return { x: 0, y: 0 };
    }

    const maxOffsetX = ((safeScale - 1) * width) / 2;
    const maxOffsetY = ((safeScale - 1) * height * 0.88) / 2;

    return {
      x: Math.max(-maxOffsetX, Math.min(maxOffsetX, x)),
      y: Math.max(-maxOffsetY, Math.min(maxOffsetY, y)),
    };
  }, []);

  const resetImageZoom = useCallback(() => {
    zoomLastScaleRef.current = 1;
    zoomLastPanRef.current = { x: 0, y: 0 };
    setIsImagePannable(false);
    zoomBaseScale.setValue(1);
    zoomPinchScale.setValue(1);
    zoomBaseTranslateX.setValue(0);
    zoomBaseTranslateY.setValue(0);
    zoomPanTranslateX.setValue(0);
    zoomPanTranslateY.setValue(0);
  }, [zoomBaseScale, zoomPinchScale, zoomBaseTranslateX, zoomBaseTranslateY, zoomPanTranslateX, zoomPanTranslateY]);

  const onZoomGestureStateChange = useCallback((event) => {
    if (event?.nativeEvent?.oldState !== GestureState.ACTIVE) {
      return;
    }

    const gestureScale = Number(event.nativeEvent.scale || 1);
    const nextScale = Math.max(1, Math.min(4, zoomLastScaleRef.current * gestureScale));

    zoomLastScaleRef.current = nextScale;
    setIsImagePannable(nextScale > 1.01);
    zoomBaseScale.setValue(nextScale);
    zoomPinchScale.setValue(1);

    const clampedPan = clampZoomOffset(
      zoomLastPanRef.current.x,
      zoomLastPanRef.current.y,
      nextScale
    );

    zoomLastPanRef.current = clampedPan;
    zoomBaseTranslateX.setValue(clampedPan.x);
    zoomBaseTranslateY.setValue(clampedPan.y);
  }, [zoomBaseScale, zoomPinchScale, zoomBaseTranslateX, zoomBaseTranslateY, clampZoomOffset]);

  const onPanGestureStateChange = useCallback((event) => {
    if (event?.nativeEvent?.oldState !== GestureState.ACTIVE) {
      return;
    }

    const currentScale = zoomLastScaleRef.current;
    if (currentScale <= 1.01) {
      zoomPanTranslateX.setValue(0);
      zoomPanTranslateY.setValue(0);
      return;
    }

    const translationX = Number(event.nativeEvent.translationX || 0);
    const translationY = Number(event.nativeEvent.translationY || 0);
    const nextPan = clampZoomOffset(
      zoomLastPanRef.current.x + translationX,
      zoomLastPanRef.current.y + translationY,
      currentScale
    );

    zoomLastPanRef.current = nextPan;
    zoomBaseTranslateX.setValue(nextPan.x);
    zoomBaseTranslateY.setValue(nextPan.y);
    zoomPanTranslateX.setValue(0);
    zoomPanTranslateY.setValue(0);
  }, [clampZoomOffset, zoomBaseTranslateX, zoomBaseTranslateY, zoomPanTranslateX, zoomPanTranslateY]);

  const closeImageZoom = useCallback(() => {
    resetImageZoom();
    setZoomImageUri('');
  }, [resetImageZoom]);

  const openImageZoom = useCallback((imageUri) => {
    const normalizedUri = String(imageUri || '').trim();
    if (!normalizedUri) {
      return;
    }

    resetImageZoom();
    setZoomImageUri(normalizedUri);
  }, [resetImageZoom]);

  const buildPendingAttachment = useCallback((asset) => {
    if (!asset?.uri || !asset?.base64) {
      return null;
    }

    const mimeType = String(asset.mimeType || 'image/jpeg').trim();
    const dataUrl = `data:${mimeType};base64,${asset.base64}`;

    return {
      previewUri: asset.uri,
      dataUrl,
      mimeType,
    };
  }, []);

  const openCameraAttachment = useCallback(async () => {
    if (isSending || isBootstrapping || (hasLiveConversation && !conversationId)) {
      return;
    }

    try {
      const cameraPermission = await ImagePicker.requestCameraPermissionsAsync();
      if (cameraPermission.status !== 'granted') {
        Alert.alert('גישה נדחתה', 'צריך לאפשר גישה למצלמה כדי לצלם ולשלוח תמונה בצ׳אט.');
        return;
      }

      const result = await ImagePicker.launchCameraAsync({
        mediaTypes: ImagePicker.MediaTypeOptions.Images,
        allowsEditing: true,
        quality: 0.55,
        base64: true,
      });

      if (result.canceled) {
        return;
      }

      const capturedAsset = result.assets?.[0];
      const pending = buildPendingAttachment(capturedAsset);
      if (!pending) {
        Alert.alert('שגיאת צילום', 'לא ניתן להכין את התמונה לשליחה. נסו לצלם שוב.');
        return;
      }

      setPendingAttachment(pending);
    } catch (_error) {
      Alert.alert('שגיאה', 'לא ניתן לפתוח את המצלמה כרגע.');
    }
  }, [isSending, isBootstrapping, hasLiveConversation, conversationId, buildPendingAttachment]);

  const openGalleryAttachment = useCallback(async () => {
    if (isSending || isBootstrapping || (hasLiveConversation && !conversationId)) {
      return;
    }

    try {
      const galleryPermission = await ImagePicker.requestMediaLibraryPermissionsAsync();
      if (galleryPermission.status !== 'granted') {
        Alert.alert('גישה נדחתה', 'צריך לאפשר גישה לגלריה כדי לבחור תמונה ולשלוח בצ׳אט.');
        return;
      }

      const result = await ImagePicker.launchImageLibraryAsync({
        mediaTypes: ImagePicker.MediaTypeOptions.Images,
        allowsEditing: true,
        quality: 0.55,
        base64: true,
      });

      if (result.canceled) {
        return;
      }

      const selectedAsset = result.assets?.[0];
      const pending = buildPendingAttachment(selectedAsset);
      if (!pending) {
        Alert.alert('שגיאת גלריה', 'לא ניתן להכין את התמונה שנבחרה לשליחה. נסו תמונה אחרת.');
        return;
      }

      setPendingAttachment(pending);
    } catch (_error) {
      Alert.alert('שגיאה', 'לא ניתן לפתוח את הגלריה כרגע.');
    }
  }, [isSending, isBootstrapping, hasLiveConversation, conversationId, buildPendingAttachment]);

  const loadConversationMessages = useCallback(async (targetConversationId, options = {}) => {
    const { showAlertOnFailure = false } = options;

    if (!hasLiveConversation || !targetConversationId) {
      return;
    }

    try {
      const requestId = latestMessagesRequestIdRef.current + 1;
      latestMessagesRequestIdRef.current = requestId;

      const baseMessagesUrl = `${API_BASE_URL}/chat/conversations/${targetConversationId}/messages`;
      const requestUrl = `${baseMessagesUrl}${baseMessagesUrl.includes('?') ? '&' : '?'}t=${Date.now()}`;

      const messagesResponse = await fetch(requestUrl, {
        cache: 'no-store',
      });
      const messagesPayload = await messagesResponse.json().catch(() => null);

      if (requestId !== latestMessagesRequestIdRef.current) {
        return;
      }

      if (!messagesResponse.ok) {
        throw new Error(messagesPayload?.message || 'Failed to load conversation messages.');
      }

      const normalizedMessages = Array.isArray(messagesPayload)
        ? messagesPayload.map(mapApiMessageToUi)
        : [];

      setMessages((prev) => {
        const nowMs = Date.now();
        const transientLocalMessages = prev.filter((msg) => {
          if (msg?.isPending || msg?.isFailed) {
            return true;
          }

          if (!msg?.isLocalEcho) {
            return false;
          }

          const sentAtMs = new Date(msg?.timestamp || 0).getTime();
          if (!Number.isFinite(sentAtMs)) {
            return false;
          }

          return nowMs - sentAtMs <= LOCAL_ECHO_GRACE_MS;
        });

        const mergedMessages = [...normalizedMessages];

        transientLocalMessages.forEach((localMessage) => {
          const alreadyExists = mergedMessages.some((serverMessage) => serverMessage.id === localMessage.id);
          if (!alreadyExists) {
            mergedMessages.push(localMessage);
          }
        });

        if (
          prev.length === mergedMessages.length
          && prev.every((msg, index) => {
            const nextMessage = mergedMessages[index];
            return msg.id === nextMessage?.id
              && Boolean(msg?.isPending) === Boolean(nextMessage?.isPending)
              && Boolean(msg?.isFailed) === Boolean(nextMessage?.isFailed);
          })
        ) {
          return prev;
        }

        return mergedMessages;
      });
    } catch (error) {
      if (showAlertOnFailure) {
        Alert.alert("שגיאת צ'אט", 'לא ניתן לפתוח את השיחה כרגע.');
      }
    }
  }, [hasLiveConversation, mapApiMessageToUi]);

  useEffect(() => {
    let isMounted = true;

    const bootstrapConversation = async () => {
      if (!hasLiveConversation) {
        return;
      }

      try {
        setIsBootstrapping(true);

        let resolvedConversationId = initialConversationId;

        if (!resolvedConversationId) {
          const conversationResponse = await fetch(`${API_BASE_URL}/chat/conversations/get-or-create`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ parentId, childId, instructorId }),
          });

          const conversationPayload = await conversationResponse.json();
          if (!conversationResponse.ok) {
            throw new Error(conversationPayload?.message || 'Failed to create or fetch conversation.');
          }

          resolvedConversationId = Number(conversationPayload?.conversationId ?? 0);
        }

        if (!resolvedConversationId) {
          throw new Error('Conversation id was not returned from the server.');
        }

        if (!isMounted) {
          return;
        }

        setConversationId(resolvedConversationId);
        await loadConversationMessages(resolvedConversationId, { showAlertOnFailure: true });
      } catch (error) {
        if (isMounted) {
          Alert.alert("שגיאת צ'אט", 'לא ניתן לפתוח את השיחה כרגע.');
        }
      } finally {
        if (isMounted) {
          setIsBootstrapping(false);
        }
      }
    };

    bootstrapConversation();

    return () => {
      isMounted = false;
    };
  }, [hasLiveConversation, parentId, childId, instructorId, initialConversationId, loadConversationMessages]);

  useEffect(() => {
    if (!hasLiveConversation || !conversationId) {
      return;
    }

    let disposed = false;

    const pollMessages = async () => {
      if (disposed || isSending || isBootstrapping) {
        return;
      }

      await loadConversationMessages(conversationId);
    };

    const intervalId = setInterval(pollMessages, MESSAGE_POLL_INTERVAL_MS);
    pollMessages();

    return () => {
      disposed = true;
      clearInterval(intervalId);
    };
  }, [hasLiveConversation, conversationId, isSending, isBootstrapping, loadConversationMessages]);

  useEffect(() => {
    if (!hasLiveConversation || fromInstructor || parentId <= 0 || instructorId <= 0) {
      return;
    }

    markParentChatNotificationsAsRead(parentId, instructorId).catch(() => {
      // Do not block chat opening if notification marking fails.
    });
  }, [hasLiveConversation, fromInstructor, parentId, instructorId]);

  useEffect(() => {
    didInitialAutoScrollRef.current = false;
  }, [conversationId]);

  useEffect(() => {
    if (!conversationId || messages.length === 0) {
      return;
    }

    const shouldAnimate = didInitialAutoScrollRef.current;
    scrollToLatestMessage(shouldAnimate);

    if (!didInitialAutoScrollRef.current) {
      didInitialAutoScrollRef.current = true;

      const settleScrollTimeout = setTimeout(() => {
        scrollToLatestMessage(false);
      }, 80);

      return () => clearTimeout(settleScrollTimeout);
    }

    return undefined;
  }, [conversationId, messages.length, scrollToLatestMessage]);

  useEffect(() => {
    if (!hasLiveConversation && isTyping) {
      Animated.loop(
        Animated.sequence([
          Animated.timing(typingPulse, { toValue: 1, duration: 600, useNativeDriver: true }),
          Animated.timing(typingPulse, { toValue: 0.4, duration: 600, useNativeDriver: true }),
        ]),
      ).start();
    } else {
      typingPulse.setValue(0.4);
    }
  }, [isTyping, hasLiveConversation]);

  const handleSend = useCallback(async () => {
    const trimmed = input.trim();
    if (!trimmed) return;

    if (hasLiveConversation) {
      if (!conversationId || isSending || isBootstrapping) {
        return;
      }

      const pendingMessageId = `pending-${Date.now()}`;
      const optimisticSenderName = fromInstructor
        ? (String(instructorName || '').trim() || 'מדריך')
        : (String(parentName || '').trim() || 'הורה');

      setMessages((prev) => [
        ...prev,
        {
          id: pendingMessageId,
          senderId: currentUserId,
          senderName: optimisticSenderName,
          text: trimmed,
          attachmentType: '',
          attachmentUrl: '',
          timestamp: new Date().toISOString(),
          isPending: true,
          isFailed: false,
        },
      ]);
      setInput('');

      try {
        setIsSending(true);

        const response = await fetch(`${API_BASE_URL}/chat/conversations/${conversationId}/messages`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            senderId: fromInstructor ? instructorId : parentId,
            senderType: fromInstructor ? 'Instructor' : 'Parent',
            messageText: trimmed,
          }),
        });

        const payload = await response.json().catch(() => null);
        if (!response.ok) {
          throw new Error(payload?.message || 'Failed to send message.');
        }

        if (!payload) {
          throw new Error('השרת החזיר תשובה לא צפויה בעת שליחת ההודעה.');
        }

        const newMsg = {
          ...mapApiMessageToUi(payload),
          isLocalEcho: true,
        };
        setMessages((prev) => {
          const withoutPending = prev.filter((msg) => msg.id !== pendingMessageId);
          return [...withoutPending, newMsg];
        });

        setTimeout(() => {
          void loadConversationMessages(conversationId);
        }, 350);
      } catch (error) {
        setMessages((prev) => prev.map((msg) => {
          if (msg.id !== pendingMessageId) {
            return msg;
          }

          return {
            ...msg,
            isPending: false,
            isFailed: true,
          };
        }));

        const errorMessage = String(error?.message || '').trim();
        Alert.alert('שגיאת שליחה', errorMessage || 'לא ניתן לשלוח את ההודעה כרגע.');
      } finally {
        setIsSending(false);
      }

      return;
    }

    const newMsg = {
      id: String(Date.now()),
      senderId: currentUserId,
      senderName: fromInstructor ? 'שי' : 'ענבל',
      text: trimmed,
      timestamp: new Date().toISOString(),
    };

    setMessages((prev) => [...prev, newMsg]);
    setInput('');

    // Simulate typing indicator + auto-reply
    setTimeout(() => setIsTyping(true), 800);
    setTimeout(() => {
      setIsTyping(false);
      const reply = {
        id: String(Date.now() + 1),
        senderId: fromInstructor ? 'parent1' : 'instructor1',
        senderName: fromInstructor ? 'ענבל' : 'שי',
        text: fromInstructor
          ? 'תודה רבה על העדכון! 🙏'
          : 'מעולה, תודה על המידע! נתראה בשיעור הבא 🏊',
        timestamp: new Date().toISOString(),
      };
      setMessages((prev) => [...prev, reply]);
    }, 2500);
  }, [
    input,
    currentUserId,
    fromInstructor,
    hasLiveConversation,
    conversationId,
    isSending,
    isBootstrapping,
    currentUserId,
    instructorName,
    parentName,
    instructorId,
    parentId,
    mapApiMessageToUi,
    loadConversationMessages,
  ]);

  const handleSendAttachment = useCallback(async () => {
    if (!pendingAttachment || isSending || isBootstrapping) {
      return;
    }

    const captionText = input.trim();

    if (hasLiveConversation) {
      if (!conversationId) {
        return;
      }

      try {
        setIsSending(true);

        const response = await fetch(`${API_BASE_URL}/chat/conversations/${conversationId}/messages`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            senderId: fromInstructor ? instructorId : parentId,
            senderType: fromInstructor ? 'Instructor' : 'Parent',
            messageText: captionText,
            attachmentImageDataUrl: pendingAttachment.dataUrl,
            attachmentMimeType: pendingAttachment.mimeType,
          }),
        });

        const payload = await response.json().catch(() => null);
        if (!response.ok) {
          throw new Error(payload?.message || 'Failed to send attachment.');
        }

        if (!payload) {
          throw new Error('השרת החזיר תשובה לא צפויה בעת שליחת התמונה.');
        }

        const newAttachmentMessage = {
          ...mapApiMessageToUi(payload),
          isLocalEcho: true,
        };
        setMessages((prev) => [...prev, newAttachmentMessage]);
        setInput('');
        setPendingAttachment(null);

        setTimeout(() => {
          void loadConversationMessages(conversationId);
        }, 350);
      } catch (_error) {
        const errorMessage = String(_error?.message || '').trim();
        Alert.alert('שגיאת שליחה', errorMessage || 'לא ניתן לשלוח את התמונה כרגע.');
      } finally {
        setIsSending(false);
      }

      return;
    }

    const newAttachmentMessage = {
      id: String(Date.now()),
      senderId: currentUserId,
      senderName: fromInstructor ? 'שי' : 'ענבל',
      text: captionText,
      attachmentType: 'image',
      attachmentUrl: pendingAttachment.previewUri,
      timestamp: new Date().toISOString(),
    };

    setMessages((prev) => [...prev, newAttachmentMessage]);
    setInput('');
    setPendingAttachment(null);

    setTimeout(() => setIsTyping(true), 800);
    setTimeout(() => {
      setIsTyping(false);
      const reply = {
        id: String(Date.now() + 1),
        senderId: fromInstructor ? 'parent1' : 'instructor1',
        senderName: fromInstructor ? 'ענבל' : 'שי',
        text: fromInstructor
          ? 'קיבלתי את התמונה, תודה 🙏'
          : 'תודה על התמונה, מעדכן אחרי בדיקה 👌',
        timestamp: new Date().toISOString(),
      };
      setMessages((prev) => [...prev, reply]);
    }, 2500);
  }, [
    pendingAttachment,
    isSending,
    isBootstrapping,
    input,
    hasLiveConversation,
    conversationId,
    fromInstructor,
    instructorId,
    parentId,
    mapApiMessageToUi,
    currentUserId,
    loadConversationMessages,
  ]);

  const currentLiveUserType = fromInstructor ? 'Instructor' : 'Parent';
  const currentLiveUserId = fromInstructor ? instructorId : parentId;

  useEffect(() => {
    if (!hasLiveConversation || currentLiveUserId <= 0) {
      return;
    }

    registerPushNotificationsForUser({
      userType: currentLiveUserType,
      userId: currentLiveUserId,
    }).then((result) => {
      if (!result?.ok) {
        console.log('[push] registration skipped', {
          userType: currentLiveUserType,
          userId: currentLiveUserId,
          reason: result?.reason || 'unknown',
        });
      }
    }).catch((error) => {
      console.warn('[push] registration failed', {
        userType: currentLiveUserType,
        userId: currentLiveUserId,
        error: String(error?.message || error),
      });
    });
  }, [hasLiveConversation, currentLiveUserType, currentLiveUserId]);

  const navBg = scrollY.interpolate({
    inputRange: [0, 50],
    outputRange: ["rgba(0, 21, 41, 0.4)", "rgba(0, 21, 41, 0.88)"],
    extrapolate: "clamp",
  });

  const renderMessage = useCallback(({ item }) => (
    <MessageBubble
      item={item}
      isMe={item.senderId === currentUserId}
      onImagePress={openImageZoom}
    />
  ), [currentUserId, openImageZoom]);

  const keyExtractor = useCallback((item) => item.id, []);

  return (
    <View style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="transparent" translucent />

      {/* Deep Ocean Base */}
      <LinearGradient colors={["#005a80", "#00456a", colors.bgDeep]} style={StyleSheet.absoluteFill} />

      {/* Surface Water */}
      <View style={StyleSheet.absoluteFill}>
        <LinearGradient colors={["#00aed8", "#007fa7", "#004d73"]} style={StyleSheet.absoluteFill} />
      </View>

      {/* Bubbles */}
      {bubblePool.map((id) => (
        <Bubble key={id} />
      ))}

      {/* Waves */}
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

      {/* NAVBAR */}
      <AnimatedBlurView intensity={20} tint="dark" style={[styles.navbar, { backgroundColor: navBg }]}>
        <View style={styles.navLeftControls}>
          <TouchableOpacity onPress={() => navigation.goBack()} style={styles.backButton}>
            <Text style={styles.backArrow}>→</Text>
          </TouchableOpacity>
          <LinearGradient colors={["#00d4ff", "#0099cc"]} style={styles.logoIcon}>
            <Text style={styles.logoEmoji}>💬</Text>
          </LinearGradient>
        </View>

        <View style={styles.navTextWrap}>
          <Text style={styles.logoText}>צ'אט</Text>
          <Text style={styles.navSubtitle}>{headerCounterpartLine}</Text>
          <Text style={styles.navSubtitle}>{headerChildLine}</Text>
        </View>
      </AnimatedBlurView>

      {/* CHAT CONTENT */}
      <KeyboardAvoidingView
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        style={styles.chatArea}
        keyboardVerticalOffset={0}
      >
        <FlatList
          ref={flatListRef}
          data={messages}
          renderItem={renderMessage}
          keyExtractor={keyExtractor}
          contentContainerStyle={styles.messagesContainer}
          showsVerticalScrollIndicator={false}
          onContentSizeChange={() => scrollToLatestMessage(false)}
          onLayout={() => scrollToLatestMessage(false)}
          ListFooterComponent={
            !hasLiveConversation && isTyping ? (
              <Animated.View style={[styles.typingRow, { opacity: typingPulse }]}>
                <LinearGradient colors={['#00d4ff', '#0077aa']} style={styles.typingAvatar}>
                  <Text style={styles.typingAvatarText}>
                    {fromInstructor ? 'ע' : 'ש'}
                  </Text>
                </LinearGradient>
                <BlurView intensity={18} tint="dark" style={styles.typingBubble}>
                  <Text style={styles.typingDots}>...</Text>
                  <Text style={styles.typingLabel}>מקליד/ה</Text>
                </BlurView>
              </Animated.View>
            ) : null
          }
        />

        {/* INPUT BAR */}
        <BlurView intensity={30} tint="dark" style={styles.inputBar}>
          <TouchableOpacity
            onPress={openGalleryAttachment}
            activeOpacity={0.82}
            disabled={isSending || isBootstrapping || (hasLiveConversation && !conversationId)}
          >
            <LinearGradient
              colors={
                isSending || isBootstrapping || (hasLiveConversation && !conversationId)
                  ? ['rgba(0,212,255,0.3)', 'rgba(0,153,204,0.3)']
                  : ['#00d4ff', '#0099cc']
              }
              style={styles.galleryBtn}
            >
              <Text style={styles.galleryBtnIcon}>🖼️</Text>
            </LinearGradient>
          </TouchableOpacity>

          <TouchableOpacity
            onPress={openCameraAttachment}
            activeOpacity={0.82}
            disabled={isSending || isBootstrapping || (hasLiveConversation && !conversationId)}
          >
            <LinearGradient
              colors={
                isSending || isBootstrapping || (hasLiveConversation && !conversationId)
                  ? ['rgba(0,212,255,0.3)', 'rgba(0,153,204,0.3)']
                  : ['#00d4ff', '#0099cc']
              }
              style={styles.cameraBtn}
            >
              <Text style={styles.cameraBtnIcon}>📷</Text>
            </LinearGradient>
          </TouchableOpacity>

          <TextInput
            style={styles.input}
            placeholder="כתבו הודעה..."
            placeholderTextColor="rgba(255,255,255,0.4)"
            value={input}
            onChangeText={setInput}
            multiline
            textAlign="right"
            editable={!isBootstrapping && !(hasLiveConversation && !conversationId)}
          />
          <TouchableOpacity
            onPress={handleSend}
            activeOpacity={0.8}
            disabled={!input.trim() || isSending || isBootstrapping || (hasLiveConversation && !conversationId)}
          >
            <LinearGradient
              colors={
                input.trim() && !isSending && !isBootstrapping
                  ? ['#00d4ff', '#0099cc']
                  : ['rgba(0,212,255,0.3)', 'rgba(0,153,204,0.3)']
              }
              style={styles.sendBtn}
            >
              <Text style={styles.sendBtnText}>{isSending ? 'שולח...' : 'שליחה'}</Text>
            </LinearGradient>
          </TouchableOpacity>
        </BlurView>
      </KeyboardAvoidingView>

      <Modal
        visible={Boolean(zoomImageUri)}
        transparent
        animationType="fade"
        onRequestClose={closeImageZoom}
      >
        <View style={styles.imageZoomOverlay}>
          <PanGestureHandler
            ref={panGestureRef}
            onGestureEvent={onPanGestureEvent}
            onHandlerStateChange={onPanGestureStateChange}
            simultaneousHandlers={pinchGestureRef}
            enabled={isImagePannable}
          >
            <Animated.View style={styles.imageZoomImageWrap}>
              <PinchGestureHandler
                ref={pinchGestureRef}
                onGestureEvent={onZoomGestureEvent}
                onHandlerStateChange={onZoomGestureStateChange}
                simultaneousHandlers={panGestureRef}
              >
                <Animated.View style={styles.imageZoomPinchWrap}>
                  {zoomImageUri ? (
                    <Animated.Image
                      source={{ uri: zoomImageUri }}
                      style={[
                        styles.imageZoomImage,
                        { transform: [{ translateX: zoomTranslateX }, { translateY: zoomTranslateY }, { scale: zoomScale }] },
                      ]}
                      resizeMode="contain"
                    />
                  ) : null}
                </Animated.View>
              </PinchGestureHandler>
            </Animated.View>
          </PanGestureHandler>

          <Text style={styles.imageZoomHint}>צביטה לזום · גרירה לתזוזה · לחצו על ✕ לסגירה</Text>

          <TouchableOpacity
            activeOpacity={0.85}
            style={styles.imageZoomCloseButton}
            onPress={closeImageZoom}
          >
            <Text style={styles.imageZoomCloseText}>✕</Text>
          </TouchableOpacity>
        </View>
      </Modal>

      <Modal
        visible={Boolean(pendingAttachment)}
        transparent
        animationType="fade"
        onRequestClose={clearPendingAttachment}
      >
        <View style={styles.attachmentModalOverlay}>
          <BlurView intensity={28} tint="dark" style={styles.attachmentModalCard}>
            <Text style={styles.attachmentModalTitle}>לשלוח את התמונה הזו?</Text>
            <Text style={styles.attachmentModalSubtitle}>אפשר לבטל, או להוסיף טקסט ואז לשלוח.</Text>

            {pendingAttachment?.previewUri ? (
              <Image
                source={{ uri: pendingAttachment.previewUri }}
                style={styles.attachmentPreviewImage}
                resizeMode="cover"
              />
            ) : null}

            <View style={styles.attachmentModalActions}>
              <TouchableOpacity
                style={styles.attachmentCancelBtn}
                activeOpacity={0.85}
                onPress={clearPendingAttachment}
                disabled={isSending}
              >
                <Text style={styles.attachmentCancelText}>ביטול</Text>
              </TouchableOpacity>

              <TouchableOpacity
                activeOpacity={0.85}
                onPress={handleSendAttachment}
                disabled={isSending}
              >
                <LinearGradient
                  colors={isSending ? ['rgba(0,212,255,0.3)', 'rgba(0,153,204,0.3)'] : ['#00d4ff', '#0099cc']}
                  style={styles.attachmentSendBtn}
                >
                  <Text style={styles.attachmentSendText}>{isSending ? 'שולח...' : 'שליחת תמונה'}</Text>
                </LinearGradient>
              </TouchableOpacity>
            </View>
          </BlurView>
        </View>
      </Modal>
    </View>
  );
}

// ========================================
// STYLES
// ========================================
const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.bgDeep },
  chatArea: { flex: 1, paddingTop: Platform.OS === 'ios' ? 110 : (StatusBar.currentHeight || 20) + 75 },
  waveContainer: {
    position: "absolute",
    bottom: 0,
    left: 0,
    width: 2400,
    height: 150,
    zIndex: 0,
  },
  bubbleWrapper: { position: "absolute", zIndex: 1 },

  // --- NAVBAR ---
  navbar: {
    position: "absolute",
    top: 0,
    left: 0,
    right: 0,
    paddingTop: Platform.OS === "ios" ? 50 : (StatusBar.currentHeight || 20) + 15,
    paddingBottom: 15,
    paddingHorizontal: 20,
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    zIndex: 100,
    borderBottomWidth: 1,
    borderBottomColor: "rgba(255,255,255,0.1)",
  },
  navLeftControls: { flexDirection: "row", alignItems: "center" },
  backButton: { marginRight: 12, padding: 5 },
  backArrow: {
    color: '#fff',
    fontSize: 24,
    lineHeight: 28,
    opacity: 0.9,
  },
  logoIcon: {
    width: 38,
    height: 38,
    borderRadius: 19,
    justifyContent: "center",
    alignItems: "center",
  },
  logoEmoji: { fontSize: 18, includeFontPadding: false },
  logoText: {
    color: "#fff",
    fontSize: 20,
    fontWeight: "800",
    includeFontPadding: false,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  navTextWrap: {
    flex: 1,
    alignItems: 'flex-end',
    marginLeft: 12,
  },
  navSubtitle: {
    color: 'rgba(255,255,255,0.6)',
    fontSize: 12,
    marginTop: 2,
    textAlign: 'right',
    writingDirection: 'rtl',
  },

  // --- MESSAGES ---
  messagesContainer: {
    paddingHorizontal: 16,
    paddingTop: 10,
    paddingBottom: 10,
  },
  msgRow: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    marginBottom: 14,
  },
  msgRowRight: {
    justifyContent: 'flex-end',
  },
  msgRowLeft: {
    justifyContent: 'flex-start',
  },
  msgAvatar: {
    width: 32,
    height: 32,
    borderRadius: 16,
    justifyContent: 'center',
    alignItems: 'center',
    marginHorizontal: 8,
  },
  msgAvatarText: {
    color: '#fff',
    fontSize: 14,
    fontWeight: '700',
  },
  msgBubble: {
    maxWidth: '70%',
    borderRadius: 20,
    paddingVertical: 12,
    paddingHorizontal: 16,
    backgroundColor: 'rgba(255,255,255,0.08)',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.12)',
    overflow: 'hidden',
  },
  msgBubbleMe: {
    borderBottomRightRadius: 6,
    backgroundColor: 'rgba(0, 212, 255, 0.12)',
    borderColor: 'rgba(0, 212, 255, 0.2)',
  },
  msgBubbleOther: {
    borderBottomLeftRadius: 6,
  },
  msgSender: {
    color: '#00d4ff',
    fontSize: 12,
    fontWeight: '700',
    marginBottom: 4,
    textAlign: 'right',
  },
  msgText: {
    color: '#fff',
    fontSize: 15,
    lineHeight: 22,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  msgAttachmentWrap: {
    borderRadius: 14,
    overflow: 'hidden',
    marginBottom: 8,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.12)',
    backgroundColor: 'rgba(0,0,0,0.15)',
  },
  msgAttachmentImage: {
    width: Math.min(width * 0.58, 260),
    height: Math.min(width * 0.7, 290),
  },
  msgAttachmentLoading: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    justifyContent: 'center',
    alignItems: 'center',
    backgroundColor: 'rgba(0,0,0,0.2)',
  },
  msgAttachmentLoadingText: {
    fontSize: 24,
  },
  msgAttachmentError: {
    justifyContent: 'center',
    alignItems: 'center',
    backgroundColor: 'rgba(0,0,0,0.25)',
  },
  msgAttachmentErrorIcon: {
    fontSize: 32,
    marginBottom: 6,
  },
  msgAttachmentErrorText: {
    color: 'rgba(255,255,255,0.7)',
    fontSize: 12,
    fontWeight: '600',
    writingDirection: 'rtl',
  },
  msgAttachmentRetryText: {
    color: '#00d4ff',
    fontSize: 11,
    fontWeight: '700',
    marginTop: 4,
    writingDirection: 'rtl',
  },
  imageZoomOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0, 8, 16, 0.94)',
    justifyContent: 'center',
    alignItems: 'center',
    paddingHorizontal: 14,
    paddingTop: Platform.OS === 'ios' ? 90 : (StatusBar.currentHeight || 20) + 54,
    paddingBottom: 26,
  },
  imageZoomImageWrap: {
    width: '100%',
    height: '88%',
    justifyContent: 'center',
    alignItems: 'center',
  },
  imageZoomPinchWrap: {
    width: '100%',
    height: '100%',
    justifyContent: 'center',
    alignItems: 'center',
  },
  imageZoomImage: {
    width: '100%',
    height: '100%',
    borderRadius: 14,
    backgroundColor: 'rgba(255,255,255,0.06)',
  },
  imageZoomHint: {
    marginTop: 14,
    color: 'rgba(231,249,255,0.8)',
    fontSize: 13,
    fontWeight: '600',
    writingDirection: 'rtl',
  },
  imageZoomCloseButton: {
    position: 'absolute',
    top: Platform.OS === 'ios' ? 50 : (StatusBar.currentHeight || 20) + 12,
    right: 16,
    width: 38,
    height: 38,
    borderRadius: 19,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.35)',
    backgroundColor: 'rgba(0, 212, 255, 0.18)',
    justifyContent: 'center',
    alignItems: 'center',
  },
  imageZoomCloseText: {
    color: '#ffffff',
    fontSize: 22,
    lineHeight: 24,
    fontWeight: '700',
  },
  msgTime: {
    color: 'rgba(255,255,255,0.4)',
    fontSize: 10,
    marginTop: 6,
    textAlign: 'right',
  },
  msgTimeMe: {
    textAlign: 'left',
  },
  msgDeliveryState: {
    marginTop: 3,
    fontSize: 10,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  msgDeliveryPending: {
    color: 'rgba(255,255,255,0.55)',
  },
  msgDeliveryFailed: {
    color: '#ff8b8b',
    fontWeight: '700',
  },

  // --- TYPING INDICATOR ---
  typingRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingLeft: 0,
    marginBottom: 10,
  },
  typingAvatar: {
    width: 28,
    height: 28,
    borderRadius: 14,
    justifyContent: 'center',
    alignItems: 'center',
    marginRight: 8,
  },
  typingAvatarText: {
    color: '#fff',
    fontSize: 12,
    fontWeight: '700',
  },
  typingBubble: {
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: 16,
    paddingVertical: 8,
    paddingHorizontal: 14,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.1)',
    overflow: 'hidden',
    gap: 6,
  },
  typingDots: {
    color: '#00d4ff',
    fontSize: 20,
    fontWeight: '800',
    letterSpacing: 3,
    lineHeight: 22,
  },
  typingLabel: {
    color: 'rgba(255,255,255,0.5)',
    fontSize: 11,
  },

  // --- INPUT BAR ---
  inputBar: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    paddingHorizontal: 14,
    paddingVertical: 10,
    borderTopWidth: 1,
    borderTopColor: 'rgba(255,255,255,0.1)',
    backgroundColor: 'rgba(0, 21, 41, 0.6)',
    overflow: 'hidden',
    gap: 10,
  },
  input: {
    flex: 1,
    color: '#fff',
    fontSize: 15,
    maxHeight: 100,
    minHeight: 44,
    paddingHorizontal: 16,
    paddingVertical: 10,
    backgroundColor: 'rgba(255,255,255,0.06)',
    borderRadius: 22,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.12)',
  },
  sendBtn: {
    paddingHorizontal: 20,
    paddingVertical: 12,
    borderRadius: 22,
    justifyContent: 'center',
    alignItems: 'center',
    minHeight: 44,
  },
  cameraBtn: {
    width: 44,
    height: 44,
    borderRadius: 22,
    justifyContent: 'center',
    alignItems: 'center',
  },
  galleryBtn: {
    width: 44,
    height: 44,
    borderRadius: 22,
    justifyContent: 'center',
    alignItems: 'center',
  },
  cameraBtnIcon: {
    fontSize: 20,
    lineHeight: 22,
  },
  galleryBtnIcon: {
    fontSize: 17,
    lineHeight: 20,
  },
  sendBtnText: {
    color: '#001529',
    fontWeight: '800',
    fontSize: 15,
  },
  attachmentModalOverlay: {
    flex: 1,
    backgroundColor: 'rgba(0, 10, 20, 0.55)',
    justifyContent: 'center',
    alignItems: 'center',
    paddingHorizontal: 20,
  },
  attachmentModalCard: {
    width: '100%',
    maxWidth: 380,
    borderRadius: 20,
    overflow: 'hidden',
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.14)',
    backgroundColor: 'rgba(0, 21, 41, 0.75)',
    padding: 14,
  },
  attachmentModalTitle: {
    color: '#ffffff',
    fontSize: 18,
    fontWeight: '800',
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  attachmentModalSubtitle: {
    marginTop: 4,
    color: 'rgba(255,255,255,0.72)',
    fontSize: 12,
    textAlign: 'right',
    writingDirection: 'rtl',
  },
  attachmentPreviewImage: {
    width: '100%',
    height: 320,
    borderRadius: 14,
    marginTop: 12,
    backgroundColor: 'rgba(255,255,255,0.08)',
  },
  attachmentModalActions: {
    marginTop: 12,
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: 10,
  },
  attachmentCancelBtn: {
    flex: 1,
    height: 44,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: 'rgba(255,255,255,0.2)',
    backgroundColor: 'rgba(255,255,255,0.08)',
    justifyContent: 'center',
    alignItems: 'center',
  },
  attachmentCancelText: {
    color: '#e7f9ff',
    fontSize: 14,
    fontWeight: '700',
    writingDirection: 'rtl',
  },
  attachmentSendBtn: {
    flex: 1,
    height: 44,
    borderRadius: 14,
    justifyContent: 'center',
    alignItems: 'center',
    paddingHorizontal: 14,
  },
  attachmentSendText: {
    color: '#001529',
    fontSize: 14,
    fontWeight: '800',
    writingDirection: 'rtl',
  },
});







