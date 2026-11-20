var YvsimXunfeiIatLibrary = {
  $YvsimXunfeiIat: {
    socket: null,
    stateCallback: 0,
    messageCallback: 0,
    decoder: null,
    encoder: null,

    decodeHeapUtf8: function (pointer, length) {
      if (!YvsimXunfeiIat.decoder) {
        YvsimXunfeiIat.decoder = new TextDecoder('utf-8', { fatal: true });
      }

      return YvsimXunfeiIat.decoder.decode(
        HEAPU8.subarray(pointer, pointer + length)
      );
    },

    allocateUtf8: function (value) {
      if (!YvsimXunfeiIat.encoder) {
        YvsimXunfeiIat.encoder = new TextEncoder();
      }

      var bytes = YvsimXunfeiIat.encoder.encode(value || '');
      var pointer = _malloc(Math.max(bytes.length, 1));
      if (!pointer) {
        throw new Error('Failed to allocate Xunfei callback memory.');
      }

      if (bytes.length > 0) {
        HEAPU8.set(bytes, pointer);
      }

      return { pointer: pointer, length: bytes.length };
    },

    notifyState: function (state, message) {
      if (!YvsimXunfeiIat.stateCallback) {
        return;
      }

      var encoded = YvsimXunfeiIat.allocateUtf8(message || '');
      try {
        {{{ makeDynCall('viii', 'YvsimXunfeiIat.stateCallback') }}}(
          state,
          encoded.pointer,
          encoded.length
        );
      } finally {
        _free(encoded.pointer);
      }
    },

    notifyMessage: function (message) {
      if (!YvsimXunfeiIat.messageCallback) {
        return;
      }

      var encoded = YvsimXunfeiIat.allocateUtf8(message || '');
      try {
        {{{ makeDynCall('vii', 'YvsimXunfeiIat.messageCallback') }}}(
          encoded.pointer,
          encoded.length
        );
      } finally {
        _free(encoded.pointer);
      }
    },

    close: function () {
      var socket = YvsimXunfeiIat.socket;
      YvsimXunfeiIat.socket = null;
      if (socket) {
        socket.onopen = null;
        socket.onmessage = null;
        socket.onerror = null;
        socket.onclose = null;
        try {
          socket.close(1000, 'Unity closed the recognition session.');
        } catch (error) {
        }
      }
    }
  },

  YvsimXunfeiIat_RegisterCallbacks__deps: ['$YvsimXunfeiIat'],
  YvsimXunfeiIat_RegisterCallbacks: function (stateCallback, messageCallback) {
    YvsimXunfeiIat.stateCallback = stateCallback;
    YvsimXunfeiIat.messageCallback = messageCallback;
  },

  YvsimXunfeiIat_Open__deps: ['$YvsimXunfeiIat'],
  YvsimXunfeiIat_Open: function (urlPointer, urlLength) {
    YvsimXunfeiIat.close();

    var url;
    try {
      url = YvsimXunfeiIat.decodeHeapUtf8(urlPointer, urlLength);
      var socket = new WebSocket(url);
      YvsimXunfeiIat.socket = socket;
      socket.onopen = function () {
        if (YvsimXunfeiIat.socket === socket) {
          YvsimXunfeiIat.notifyState(0, '');
        }
      };
      socket.onmessage = function (event) {
        if (YvsimXunfeiIat.socket === socket) {
          YvsimXunfeiIat.notifyMessage(String(event.data || ''));
        }
      };
      socket.onerror = function () {
        if (YvsimXunfeiIat.socket === socket) {
          YvsimXunfeiIat.notifyState(1, 'Browser WebSocket connection failed.');
        }
      };
      socket.onclose = function (event) {
        if (YvsimXunfeiIat.socket === socket) {
          YvsimXunfeiIat.socket = null;
          YvsimXunfeiIat.notifyState(
            2,
            'WebSocket closed. code=' + event.code + ', reason=' + (event.reason || '')
          );
        }
      };
      return 1;
    } catch (error) {
      YvsimXunfeiIat.close();
      YvsimXunfeiIat.notifyState(1, 'Failed to open browser WebSocket: ' + String(error));
      return 0;
    }
  },

  YvsimXunfeiIat_Send__deps: ['$YvsimXunfeiIat'],
  YvsimXunfeiIat_Send: function (messagePointer, messageLength) {
    var socket = YvsimXunfeiIat.socket;
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return 0;
    }

    try {
      socket.send(YvsimXunfeiIat.decodeHeapUtf8(messagePointer, messageLength));
      return 1;
    } catch (error) {
      YvsimXunfeiIat.notifyState(1, 'Browser WebSocket send failed: ' + String(error));
      return 0;
    }
  },

  YvsimXunfeiIat_Close__deps: ['$YvsimXunfeiIat'],
  YvsimXunfeiIat_Close: function () {
    YvsimXunfeiIat.close();
  }
};

mergeInto(LibraryManager.library, YvsimXunfeiIatLibrary);
